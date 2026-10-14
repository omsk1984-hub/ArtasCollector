using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Security;
using System.Security.Authentication;
using ArtasCore.Abstractions;
using ArtasWriter.Core;
using ArtasWriter.FinamApi.Options;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Tradeapi.V1.Auth;
using Grpc.Tradeapi.V1.Marketdata;
using Microsoft.Extensions.Logging;

namespace ArtasWriter.FinamApi;

/// <summary>
/// Реализация <see cref="IFinamClient"/> поверх gRPC Finam Trade API.
///
/// Транспорт: gRPC Server Streaming (<c>MarketDataService.SubscribeQuote</c>,
/// <c>SubscribeOrderBook</c>) по адресу <see cref="FinamOptions.GrpcUrl"/>.
/// Авторизация: secret key → JWT через <c>AuthService.Auth</c>, далее JWT в
/// metadata-заголовке <c>Authorization</c>.
///
/// Open interest приходит в составе <c>Quote.open_interest</c>: при его наличии
/// дополнительно публикуется событие <see cref="OpenInterestReceived"/>.
/// </summary>
public sealed class FinamGrpcClient : IFinamClient
{
    private readonly FinamOptions _options;
    private readonly ILogger<FinamGrpcClient> _logger;
    private readonly GrpcChannel _channel;
    private readonly AuthService.AuthServiceClient _authClient;
    private readonly MarketDataService.MarketDataServiceClient _marketDataClient;

    private readonly Dictionary<string, Subscription> _subscriptions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    // --- Единая авторизация на клиент (auth-keepalive) --------------------
    // JWT общий для всех символов/стримов; обновляется одним фоновым циклом.
    private readonly SemaphoreSlim _authGate = new(1, 1);

    // Сигнал «немедленно переавторизоваться» (0..1): памп будит keepalive при Unauthenticated.
    private readonly SemaphoreSlim _reauthSignal = new(0, 1);

    private CancellationTokenSource? _authCts;
    private Task? _authLoopTask;

    // Токен читается памами атомарно, меняется только в Auth под _authGate.
    private volatile string? _token;
    private volatile bool _connected;
    private volatile bool _disposed;
    private string? _lastInterestSymbol;
    private long _lastInterestValue;

    /// <summary>
    /// Accumulated order books per symbol. Finam stream sends deltas
    /// (<c>ACTION_ADD</c>/<c>ACTION_UPDATE</c>/<c>ACTION_REMOVE</c>) and full
    /// snapshots (<c>is_data_snapshot=true</c>); we materialise the full book
    /// so <c>MarketService</c> always sees a complete, centred order book.
    /// </summary>
    private readonly ConcurrentDictionary<string, SymbolOrderBook> _orderBooks = new(StringComparer.Ordinal);

    /// <summary>
    /// Активная подписка: связанный токен (внешний + внутренний) и фоновая задача стрима.
    /// </summary>
    private sealed class Subscription
    {
        public required CancellationTokenSource Cts { get; init; }
        public required Task Task { get; init; }
    }

    public FinamGrpcClient(FinamOptions options, ILogger<FinamGrpcClient> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var handler = new SocketsHttpHandler
        {
            // В среде проекта TLS 1.3 недоступен (сервер/клиент не сходятся),
            // поэтому явно ограничиваемся TLS 1.2, поддерживаемым api.finam.ru.
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12,
            },
            // Соединение с Finam долгоживущее (стеки котировок): не рвём по таймауту.
            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(15),
            EnableMultipleHttp2Connections = true,
        };

        _channel = GrpcChannel.ForAddress(_options.GrpcUrl, new GrpcChannelOptions
        {
            HttpHandler = handler,
            DisposeHttpClient = true,
        });

        _authClient = new AuthService.AuthServiceClient(_channel);
        _marketDataClient = new MarketDataService.MarketDataServiceClient(_channel);
    }

    /// <inheritdoc />
    public event Action<QuoteEvent>? QuoteReceived;

    /// <inheritdoc />
    public event Action<OrderBookEvent>? OrderBookReceived;

    /// <inheritdoc />
    public event Action<OpenInterestEvent>? OpenInterestReceived;

    /// <inheritdoc />
    public event Action<StreamFailedEvent>? StreamFailed;

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Token))
        {
            throw new InvalidOperationException(
                "Finam:Token (secret key) не задан. Укажите его в appsettings.json или в переменной окружения Finam__Token.");
        }

        // Первичная авторизация: валидируем ключ и получаем первый JWT.
        if (!await TryAuthOnceAsync(cancellationToken))
        {
            throw new InvalidOperationException("Finam Auth не вернул JWT-токен.");
        }

        _connected = true;
        _logger.LogInformation("Finam: JWT получен, соединение установлено");

        // Запускаем фоновый auth-keepalive (один на весь клиент) — он обновляет
        // общий JWT по таймауту и по сигналу от стримов (SignalReauth).
        _authCts ??= new CancellationTokenSource();
        _authLoopTask ??= Task.Run(() => RunAuthKeepaliveAsync(_authCts.Token));
    }

    /// <summary>
    /// Один вызов Auth под <see cref="_authGate"/>. При успехе атомарно заменяет
    /// общий <see cref="_token"/>. Используется и при старте, и keepalive-циклом.
    /// </summary>
    private async Task<bool> TryAuthOnceAsync(CancellationToken cancellationToken)
    {
        await _authGate.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Finam: авторизация через gRPC Auth ({Url})", _options.GrpcUrl);

            var response = await _authClient
                .AuthAsync(new AuthRequest { Secret = _options.Token }, cancellationToken: cancellationToken);

            if (string.IsNullOrWhiteSpace(response.Token))
            {
                _logger.LogWarning("Finam Auth вернул пустой JWT-токен");
                return false;
            }

            _token = response.Token;
            return true;
        }
        finally
        {
            _authGate.Release();
        }
    }

    /// <summary>
    /// Фоновый цикл обновления JWT: ждёт сигнала <see cref="SignalReauth"/> или
    /// таймаута <see cref="FinamOptions.AuthRefreshMinutes"/>, затем выполняет
    /// ровно один Auth. Токен общий для всех стримов, поэтому Auth сериализован.
    /// </summary>
    private async Task RunAuthKeepaliveAsync(CancellationToken cancellationToken)
    {
        var refresh = TimeSpan.FromMinutes(Math.Max(1, _options.AuthRefreshMinutes));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Ждём либо явный сигнал стрима, либо плановый таймаут обновления.
                await _reauthSignal.WaitAsync(refresh, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                if (await TryAuthOnceAsync(cancellationToken))
                {
                    _connected = true;
                    _logger.LogInformation("Finam: JWT обновлён auth-keepalive");
                }
                else
                {
                    _logger.LogWarning("Finam: auth-keepalive не смог обновить JWT, повтор позже");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Finam: ошибка auth-keepalive, повтор позже");
            }
        }
    }

    /// <summary>
    /// Разбудить auth-keepalive для немедленного обновления JWT. Вызывается из
    /// памов при получении <c>Unauthenticated</c>. Не блокирует вызывающего.
    /// </summary>
    private void SignalReauth()
    {
        // SemaphoreSlim(0,1): повторный сигнал при уже поднятом флаге игнорируется
        // (бросаем исключение — гасим), чтобы не накапливать «лишние» переавторизации.
        try
        {
            _reauthSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // уже есть непрочитанный сигнал — достаточно
        }
    }

    /// <summary>
    /// Обработка одной неудачной попытки стрима. Возвращает true, если лимит
    /// ретраев исчерпан и стрим нужно остановить (с эмитом <see cref="StreamFailed"/>).
    /// При <c>Unauthenticated</c>/<c>PermissionDenied</c> будит auth-keepalive.
    /// </summary>
    private bool HandleStreamRetryAsync(
        string symbol,
        StreamKind kind,
        string streamName,
        string? reason,
        bool isAuthError,
        ref int attempts,
        CancellationToken cancellationToken)
    {
        attempts++;

        // Токен мог протухнуть — просим keepalive обновить JWT и даём ему шанс.
        if (isAuthError && _options.ReauthOnRetry)
        {
            SignalReauth();
        }

        if (attempts > _options.StreamMaxRetries)
        {
            var text = string.IsNullOrWhiteSpace(reason) ? "превышен лимит попыток" : reason!;
            _logger.LogError(
                "Finam стрим {Stream} {Symbol} остановлен: исчерпан лимит ретраев ({Max}). Причина: {Reason}",
                streamName, symbol, _options.StreamMaxRetries, text);

            RaiseStreamFailed(symbol, kind, text);
            return true;
        }

        _logger.LogWarning(
            "Finam стрим {Stream} {Symbol}: попытка {Attempt}/{Max} не удалась ({Reason})",
            streamName, symbol, attempts, _options.StreamMaxRetries, reason ?? "нет деталей");

        return false;
    }

    /// <summary>
    /// Уведомить подписчиков о том, что стрим исчерпал лимит попыток и остановлен.
    /// </summary>
    private void RaiseStreamFailed(string symbol, StreamKind kind, string reason)
    {
        try
        {
            StreamFailed?.Invoke(new StreamFailedEvent(symbol, kind, reason));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Finam: ошибка обработчика StreamFailed для {Symbol}/{Kind}", symbol, kind);
        }
    }

    /// <summary>
    /// Публикует событие восстановления стрима (если интерфейс предполагает),
    /// логирует и используется для сброса визуального «упал» без StreamFailed.
    /// </summary>
    private void OnStreamRecovered(string symbol, StreamKind kind)
    {
        _logger.LogInformation("Finam стрим {Kind} {Symbol}: данные возобновлены", kind, symbol);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Помимо потока котировок открывает поток сделок (<c>SubscribeLatestTrades</c>):
    /// он несёт направление сделки (BUY/SELL), необходимое ядру фракталов.
    /// </remarks>
    public async Task SubscribeQuotesAsync(string symbol, CancellationToken cancellationToken = default)
    {
        await EnsureSubscriptionAsync(
            $"quotes:{symbol}",
            ct => PumpQuotesAsync(symbol, ct),
            cancellationToken);

        await EnsureSubscriptionAsync(
            $"trades:{symbol}",
            ct => PumpTradesAsync(symbol, ct),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SubscribeOrderBookAsync(string symbol, CancellationToken cancellationToken = default)
        => EnsureSubscriptionAsync(
            $"orderbook:{symbol}",
            ct => PumpOrderBookAsync(symbol, ct),
            cancellationToken);

    /// <inheritdoc />
    public Task SubscribeOpenInterestAsync(string symbol, CancellationToken cancellationToken = default)
    {
        // Открытый интерес приходит в потоке котировок (Quote.open_interest),
        // отдельной подписки в MarketDataService нет.
        _lastInterestSymbol = symbol;
        return Task.CompletedTask;
    }

    private Metadata CreateHeaders()
    {
        var token = _token ?? throw new InvalidOperationException(
            "Finam: JWT не получен. Сначала вызовите ConnectAsync.");

        // ДИАГНОСТИКА: печатаем только форму заголовка (не сам токен!), чтобы
        // понять, ожидает ли Finam префикс "Bearer ".
        _logger.LogDebug(
            "Finam: ставим заголовок Authorization (len={Len}, prefix='{Prefix}', " +
            "hasBearer={HasBearer}, hasLeadingSpace={HasLeadingSpace})",
            token.Length,
            token.Length > 6 ? token[..6] : token,
            token.StartsWith("Bearer", StringComparison.OrdinalIgnoreCase),
            token.StartsWith(' '));

        return new Metadata { { "Authorization", token } };
    }

    /// <summary>
    /// Классифицирует RPC-обрыв по статусу, чтобы понять, отклонён ли ключ
    /// на уровне метаданных (Unauthenticated/PermissionDenied) или это
    /// временная сетевая ошибка.
    /// </summary>
    private void LogStreamBreak(string streamName, string symbol, RpcException ex)
    {
        var trailers = ex.Trailers.Count > 0
            ? string.Join("; ", ex.Trailers.Select(t => $"{t.Key}={t.Value}"))
            : "<none>";

        _logger.LogWarning(ex,
            "Finam стрим {Stream} {Symbol} прерван: status={Status}, detail='{Detail}', " +
            "trailers=[{Trailers}], повтор через {Delay} мс",
            streamName, symbol, ex.StatusCode, ex.Status.Detail, trailers, _options.ReconnectDelayMs);
    }

    private async Task EnsureSubscriptionAsync(
        string key,
        Func<CancellationToken, Task> pump,
        CancellationToken cancellationToken)
    {
        if (!_connected || _token is null)
        {
            throw new InvalidOperationException(
                "Finam: подписка до ConnectAsync. Сначала вызовите ConnectAsync.");
        }

        await _gate.WaitAsync();
        try
        {
            if (_subscriptions.ContainsKey(key))
            {
                return;
            }

            // Связываем внешний токен (например, от продьюсера) с собственным,
            // чтобы отмена извне корректно останавливала стрим.
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var task = Task.Run(() => pump(cts.Token), CancellationToken.None);
            _subscriptions[key] = new Subscription { Cts = cts, Task = task };
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PumpQuotesAsync(string symbol, CancellationToken cancellationToken)
    {
        var attempts = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Finam: подписка на котировки {Symbol}", symbol);

                using var call = _marketDataClient.SubscribeQuote(
                    new SubscribeQuoteRequest { Symbols = { symbol } },
                    CreateHeaders(),
                    cancellationToken: cancellationToken);

                var received = 0;
                await foreach (var message in call.ResponseStream.ReadAllAsync(cancellationToken))
                {
                    if (message.Error is { Code: not 0 })
                    {
                        // ДИАГНОСТИКА: бизнес-ошибка внутри потока (200 OK + Error).
                        // Если ключ отклонён — увидим здесь Code/Description от Finam.
                        _logger.LogWarning(
                            "Finam стрим котировок {Symbol}: бизнес-ошибка стрима " +
                            "Code={Code}, Description='{Description}'",
                            symbol, message.Error.Code, message.Error.Description);
                        continue;
                    }

                    foreach (var quote in message.Quote)
                    {
                        received++;
                        if (received == 1)
                        {
                            // Данные пошли — считаем стрим здоровым и сбрасываем счётчик.
                            if (attempts > 0)
                            {
                                attempts = 0;
                                OnStreamRecovered(symbol, StreamKind.Quotes);
                            }

                            _logger.LogInformation(
                                "Finam стрим котировок {Symbol}: первые данные (bid={Bid} ask={Ask} last={Last})",
                                symbol, quote.Bid?.Value, quote.Ask?.Value, quote.Last?.Value);
                        }

                        // Открытый интерес публикуем РАНЬШЕ котировки из того же
                        // сообщения Finam: иначе фрактал, сформированный этой
                        // котировкой, «заморозит» старое (или нулевое) значение
                        // интереса и уйдёт в БД с interest = 0.
                        var interest = ToLong(quote.OpenInterest?.Value);
                        if (interest is not null)
                        {
                            PublishInterest(quote.Symbol, interest.Value, ToMillis(quote.Timestamp));
                        }

                        PublishQuote(quote);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (RpcException ex) when (IsShutdown(ex, cancellationToken))
            {
                // Штатное завершение при остановке: не считаем обрывом, не логируем стек.
            }
            catch (RpcException ex)
            {
                LogStreamBreak("котировок", symbol, ex);

                if (HandleStreamRetryAsync(
                        symbol, StreamKind.Quotes, "котировок", ex.Status.Detail,
                        IsAuthError(ex.StatusCode), ref attempts, cancellationToken))
                {
                    break; // лимит ретраев исчерпан — стрим остановлен
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Finam стрим котировок {Symbol}: непредвиденная ошибка", symbol);

                if (HandleStreamRetryAsync(
                        symbol, StreamKind.Quotes, "котировок", ex.Message,
                        isAuthError: false, ref attempts, cancellationToken))
                {
                    break;
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(_options.ReconnectDelayMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PumpOrderBookAsync(string symbol, CancellationToken cancellationToken)
    {
        var attempts = 0;
        var receivedAny = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Finam: подписка на стакан {Symbol}", symbol);

                using var call = _marketDataClient.SubscribeOrderBook(
                    new SubscribeOrderBookRequest { Symbol = symbol },
                    CreateHeaders(),
                    cancellationToken: cancellationToken);

                await foreach (var message in call.ResponseStream.ReadAllAsync(cancellationToken))
                {
                    foreach (var book in message.OrderBook)
                    {
                        if (!receivedAny)
                        {
                            receivedAny = true;
                            _logger.LogInformation("Finam стрим стакана {Symbol}: первые данные", symbol);
                        }

                        if (attempts > 0)
                        {
                            attempts = 0;
                            OnStreamRecovered(symbol, StreamKind.OrderBook);
                        }

                        PublishOrderBook(book);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (RpcException ex) when (IsShutdown(ex, cancellationToken))
            {
                // Штатное завершение при остановке: не считаем обрывом, не логируем стек.
            }
            catch (RpcException ex)
            {
                LogStreamBreak("стакана", symbol, ex);

                if (HandleStreamRetryAsync(
                        symbol, StreamKind.OrderBook, "стакана", ex.Status.Detail,
                        IsAuthError(ex.StatusCode), ref attempts, cancellationToken))
                {
                    break; // лимит ретраев исчерпан — стрим остановлен
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Finam стрим стакана {Symbol}: непредвиденная ошибка", symbol);

                if (HandleStreamRetryAsync(
                        symbol, StreamKind.OrderBook, "стакана", ex.Message,
                        isAuthError: false, ref attempts, cancellationToken))
                {
                    break;
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(_options.ReconnectDelayMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PumpTradesAsync(string symbol, CancellationToken cancellationToken)
    {
        var attempts = 0;
        var receivedAny = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Finam: подписка на сделки {Symbol}", symbol);

                using var call = _marketDataClient.SubscribeLatestTrades(
                    new SubscribeLatestTradesRequest { Symbol = symbol },
                    CreateHeaders(),
                    cancellationToken: cancellationToken);

                await foreach (var message in call.ResponseStream.ReadAllAsync(cancellationToken))
                {
                    foreach (var trade in message.Trades)
                    {
                        if (!receivedAny)
                        {
                            receivedAny = true;
                            _logger.LogInformation("Finam стрим сделок {Symbol}: первые данные", symbol);
                        }

                        if (attempts > 0)
                        {
                            attempts = 0;
                            OnStreamRecovered(symbol, StreamKind.Trades);
                        }

                        PublishTrade(symbol, trade);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (RpcException ex) when (IsShutdown(ex, cancellationToken))
            {
                // Штатное завершение при остановке: не считаем обрывом, не логируем стек.
            }
            catch (RpcException ex)
            {
                LogStreamBreak("сделок", symbol, ex);

                if (HandleStreamRetryAsync(
                        symbol, StreamKind.Trades, "сделок", ex.Status.Detail,
                        IsAuthError(ex.StatusCode), ref attempts, cancellationToken))
                {
                    break; // лимит ретраев исчерпан — стрим остановлен
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Finam стрим сделок {Symbol}: непредвиденная ошибка", symbol);

                if (HandleStreamRetryAsync(
                        symbol, StreamKind.Trades, "сделок", ex.Message,
                        isAuthError: false, ref attempts, cancellationToken))
                {
                    break;
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(_options.ReconnectDelayMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Публикует сделку как котировочное событие с направлением (BUY/SELL),
    /// которое необходимо ядру фракталов. Стороны берём из <c>Trade.side</c>.
    /// </summary>
    private void PublishTrade(string symbol, Trade trade)
    {
        Observability.Counter("finam.trades.received").Add(1);

        var handler = QuoteReceived;
        if (handler is null)
        {
            return;
        }

        try
        {
            var price = ToDouble(trade.Price?.Value) ?? 0d;
            var flags = ArtasCore.Models.TickFlagExtensions
                .ForTrade(MapSide(trade.Side));
            var time = ToMillis(trade.Timestamp);

            // Интерес применяем РАНЬШЕ публикации сделки-котировки, чтобы
            // фрактал из этой сделки уже видел актуальное значение интереса.
            var interest = ToLong(trade.OpenInterest?.Value);
            if (interest is not null)
            {
                PublishInterest(symbol, interest.Value, time);
            }

            handler(new QuoteEvent(
                symbol,
                0d,
                0d,
                price,
                ToInt(trade.Size?.Value),
                (int)flags,
                time));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Finam: ошибка публикации сделки {Symbol}", symbol);
        }
    }

    private void PublishQuote(Quote quote)
    {
        Observability.Counter("finam.quotes.received").Add(1);

        var handler = QuoteReceived;
        if (handler is null)
        {
            return;
        }

        try
        {
            var last = ToDouble(quote.Last?.Value);
            // Направление сделки в котировке не передаётся; помечаем как изменение last.
            var flags = last is not null
                ? ArtasCore.Models.TickFlag.Last
                : ArtasCore.Models.TickFlag.None;

            handler(new QuoteEvent(
                quote.Symbol,
                ToDouble(quote.Bid?.Value) ?? 0d,
                ToDouble(quote.Ask?.Value) ?? 0d,
                last ?? 0d,
                ToInt(quote.LastSize?.Value),
                (int)flags,
                ToMillis(quote.Timestamp)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Finam: ошибка публикации котировки {Symbol}", quote.Symbol);
        }
    }

    private void PublishOrderBook(StreamOrderBook book)
    {
        Observability.Counter("finam.orderbooks.received").Add(1);

        var handler = OrderBookReceived;
        if (handler is null)
        {
            return;
        }

        try
        {
            // Снапшот (is_data_snapshot) — полное состояние стакана: заменяем его
            // целиком. Дельты (ADD/UPDATE/REMOVE) применяем инкрементально. Это
            // устраняет «мусорные» уровни, которые Finam добавляет, но не удаляет.
            var orderBook = _orderBooks.GetOrAdd(book.Symbol, _ => new SymbolOrderBook());
            var (bids, asks, time) = orderBook.Apply(book, ToDouble, ToInt, ToMillis);

            if (bids.Count == 0 && asks.Count == 0)
            {
                return;
            }

            // Bids по убыванию цены, asks по возрастанию — как ожидает MarketService.
            bids.Sort((a, b) => b.Price.CompareTo(a.Price));
            asks.Sort((a, b) => a.Price.CompareTo(b.Price));

            handler(new OrderBookEvent(book.Symbol, bids, asks, time));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Finam: ошибка публикации стакана {Symbol}", book.Symbol);
        }
    }

    private void PublishInterest(string symbol, long interest, long timeMsc)
    {
        if (symbol == _lastInterestSymbol && interest == _lastInterestValue)
        {
            return;
        }

        _lastInterestSymbol = symbol;
        _lastInterestValue = interest;

        OpenInterestReceived?.Invoke(new OpenInterestEvent(symbol, interest, timeMsc));
    }

    /// <summary>
    /// Штатное ли это завершение стрима. При остановке gRPC-канал утилизируется
    /// и активные вызовы завершаются с <see cref="StatusCode.Cancelled"/>
    /// ("gRPC call disposed") — это не сетевой обрыв, а плановое выключение.
    /// </summary>
    private static bool IsShutdown(RpcException ex, CancellationToken cancellationToken)
        => cancellationToken.IsCancellationRequested
           || ex.StatusCode == StatusCode.Cancelled;

    /// <summary>
    /// Отказ по авторизации/доступу ключа: сервер отклонил токен.
    /// В этом случае стоит обновить JWT через auth-keepalive (<see cref="SignalReauth"/>).
    /// </summary>
    private static bool IsAuthError(StatusCode statusCode)
        => statusCode == StatusCode.Unauthenticated
           || statusCode == StatusCode.PermissionDenied;

    /// <summary>
    /// Маппинг protobuf-направления сделки в нейтральный <see cref="ArtasCore.Models.TradeSide"/>.
    /// Всё, что не <c>Sell</c>, трактуется как покупка (как и раньше).
    /// </summary>
    private static ArtasCore.Models.TradeSide MapSide(Grpc.Tradeapi.V1.Side side)
        => side == Grpc.Tradeapi.V1.Side.Sell
            ? ArtasCore.Models.TradeSide.Sell
            : ArtasCore.Models.TradeSide.Buy;

    private static long ToMillis(Timestamp? timestamp)
        => timestamp is null ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : timestamp.ToDateTimeOffset().ToUnixTimeMilliseconds();

    private static double? ToDouble(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;

    private static int ToInt(string? value)
    {
        var parsed = ToDouble(value);
        return parsed is null ? 0 : (int)Math.Round(parsed.Value);
    }

    private static long? ToLong(string? value)
    {
        var parsed = ToDouble(value);
        return parsed is null ? null : (long)Math.Round(parsed.Value);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Быстрая защита от повторного вызова: семафор к этому моменту уже может
        // быть освобождён, поэтому проверяем флаг до обращения к _gate и _channel.
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync();
        Subscription[] subscriptions;
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            subscriptions = _subscriptions.Values.ToArray();
            _subscriptions.Clear();
        }
        finally
        {
            _gate.Release();
        }

        // Сначала отменяем все стримы, затем дожидаемся их завершения,
        // и только потом освобождаем gRPC-канал.
        foreach (var subscription in subscriptions)
        {
            try
            {
                await subscription.Cts.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
                // уже отменён/освобождён
            }
        }

        try
        {
            await Task.WhenAll(subscriptions.Select(s => s.Task))
                .WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Finam: ожидание остановки стримов: {ex.Message}");
        }

        foreach (var subscription in subscriptions)
        {
            subscription.Cts.Dispose();
        }

        // Останавливаем auth-keepalive (после стримов, чтобы он не мешал их остановке).
        if (_authCts is not null)
        {
            try
            {
                await _authCts.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
                // уже остановлен
            }

            if (_authLoopTask is not null)
            {
                try
                {
                    await _authLoopTask.WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Finam: ожидание остановки auth-keepalive: {ex.Message}");
                }
            }

            _authCts.Dispose();
            _authCts = null;
            _authLoopTask = null;
        }

        _connected = false;
        _channel.Dispose();
        _authGate.Dispose();
        _reauthSignal.Dispose();
        _gate.Dispose();
    }

    /// <summary>
    /// Материализованный стакан одного символа. Применяет инкрементальные
    /// обновления Finam (<c>ADD</c>/<c>UPDATE</c>/<c>REMOVE</c>), а также полные
    /// снапшоты (<c>is_data_snapshot = true</c>), и отдаёт полный список уровней.
    /// </summary>
    private sealed class SymbolOrderBook
    {
        private readonly Dictionary<double, int> _bids = new();
        private readonly Dictionary<double, int> _asks = new();
        private readonly object _lock = new();

        public (
            List<(double Price, int Volume)> Bids,
            List<(double Price, int Volume)> Asks,
            long Time) Apply(
                StreamOrderBook book,
                Func<string?, double?> toDouble,
                Func<string?, int> toInt,
                Func<Timestamp?, long> toMillis)
        {
            lock (_lock)
            {
                // Полный снапшот: очищаем прежнее состояние.
                if (book.IsDataSnapshot)
                {
                    _bids.Clear();
                    _asks.Clear();
                }

                long time = toMillis(book.Rows.Count > 0 ? book.Rows[0].Timestamp : null);

                foreach (var row in book.Rows)
                {
                    var price = toDouble(row.Price?.Value);
                    if (price is null)
                    {
                        continue;
                    }

                    var remove = row.Action == StreamOrderBook.Types.Row.Types.Action.Remove;

                    switch (row.SideCase)
                    {
                        case StreamOrderBook.Types.Row.SideOneofCase.BuySize:
                            ApplyLevel(_bids, price.Value, toInt(row.BuySize?.Value), remove);
                            break;
                        case StreamOrderBook.Types.Row.SideOneofCase.SellSize:
                            ApplyLevel(_asks, price.Value, toInt(row.SellSize?.Value), remove);
                            break;
                    }

                    var rowTime = toMillis(row.Timestamp);
                    if (rowTime > time)
                    {
                        time = rowTime;
                    }
                }

                return (
                    _bids.Select(kv => (kv.Key, kv.Value)).ToList(),
                    _asks.Select(kv => (kv.Key, kv.Value)).ToList(),
                    time);
            }
        }

        private static void ApplyLevel(Dictionary<double, int> levels, double price, int volume, bool remove)
        {
            if (remove || volume <= 0)
            {
                levels.Remove(price);
                return;
            }

            levels[price] = volume;
        }
    }
}
