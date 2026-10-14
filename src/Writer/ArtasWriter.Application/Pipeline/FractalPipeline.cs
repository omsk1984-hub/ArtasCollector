using System.Threading.Channels;
using ArtasWriter.Application.Producers;
using ArtasWriter.Application.Services;
using ArtasWriter.Core;
using ArtasCore.Abstractions;
using ArtasCore.Models;
using Microsoft.Extensions.Logging;

namespace ArtasWriter.Application.Pipeline;

/// <summary>
/// Запись фрактала для writer-консьюмера (общий канал записи в PG).
/// </summary>
public readonly record struct FractalRecord(int SymbolId, Fractal Fractal);

/// <summary>
/// Потребитель: читает события из канала инструмента, обрабатывает фракталы
/// и публикует результат в общий канал записи.
/// </summary>
public sealed class FractalPipeline
{
    private readonly TickService _tickService;
    private readonly int _symbolId;
    private readonly string _symbolName;
    private readonly ChannelWriter<FractalRecord> _writerChannel;
    private readonly ILogger _logger;

    public FractalPipeline(
        TickService tickService,
        int symbolId,
        ChannelWriter<FractalRecord> writerChannel,
        ILogger logger,
        string? symbolName = null)
    {
        _tickService = tickService ?? throw new ArgumentNullException(nameof(tickService));
        _symbolId = symbolId;
        _writerChannel = writerChannel ?? throw new ArgumentNullException(nameof(writerChannel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // Проектное имя символа (например "CNYRUBF") для логов/scope.
        // Fallback — symbolId как строка, чтобы поле не было пустым.
        _symbolName = symbolName ?? symbolId.ToString();
    }

    /// <summary>Обработанных тиков.</summary>
    public long ProcessedTicks { get; private set; }

    /// <summary>Записанных фракталов.</summary>
    public long WrittenFractals { get; private set; }

    /// <summary>Текущий открытый интерес инструмента.</summary>
    public long Interest => _tickService.Market.Interest;

    /// <summary>Последний обработанный тик инструмента.</summary>
    public MyTick TickCurrent => _tickService.TickCurrent;

    /// <summary>Последний стакан (Ask слева, Bid справа в центре).</summary>
    public IReadOnlyList<(double Price, int Volume)> CurrentBook =>
        _tickService.Market.CurrentBook;

    /// <summary>
    /// Запустить обработку событий из канала до его завершения.
    /// </summary>
    public async Task RunAsync(ChannelReader<FinamEvent> reader, CancellationToken cancellationToken)
    {
        // Контекст инструмента на весь цикл: любой лог внутри обработки
        // (текущий catch и будущие) несёт symbol/symbol_id структурно.
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["symbol"] = _symbolName,
            ["symbol_id"] = _symbolId,
        });

        await foreach (var evt in reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                Process(evt);
            }
            catch (Exception ex)
            {
                // Явные поля symbol=/symbol_id=/event_type= в тексте — чтобы
                // Promtail-regex извлёк их в structured metadata (scope в файл
                // по умолчанию не рендерится).
                _logger.LogError(ex,
                    "Ошибка обработки события Finam: symbol={Symbol} symbol_id={SymbolId} event_type={EventType}",
                    _symbolName, _symbolId, EventTypeOf(evt));
            }
        }
    }

    /// <summary>Определить тип события Finam строкой (для логов/метрик).</summary>
    private static string EventTypeOf(FinamEvent evt) =>
        evt.Quote is not null ? "quote" :
        evt.OrderBook is not null ? "orderbook" :
        evt.OpenInterest is not null ? "openinterest" : "unknown";

    /// <summary>Обработать одно событие (используется в тестах).</summary>
    public void Process(FinamEvent evt)
    {
        // OpenTelemetry: начинаем трейс обработки события.
        var eventType = EventTypeOf(evt);

        var span = Observability.StartSpan("pipeline.process_event")
            .SetTag("symbol_id", _symbolId)
            .SetTag("event_type", eventType);

        try
        {
            if (evt.OrderBook is { } ob)
            {
                _tickService.OnOrderBook(ob);
                return;
            }
            if (evt.OpenInterest is { } oi)
            {
                _tickService.OnOpenInterest(oi);
                return;
            }
            if (evt.Quote is { } quote)
            {
                ProcessedTicks++;
                Observability.Counter("pipeline.ticks.processed").Add(1);

                var processStart = DateTime.UtcNow;
                var fractal = _tickService.OnQuote(quote);
                var processTime = (DateTime.UtcNow - processStart).TotalMilliseconds;

                Observability.Histogram("pipeline.quote_processing_ms").Record(processTime);

                if (fractal is not null)
                {
                    WrittenFractals++;
                    Observability.Counter("pipeline.fractals.produced").Add(1);
                    _writerChannel.TryWrite(new FractalRecord(_symbolId, fractal));
                }
            }
        }
        finally
        {
            span.End();
        }
    }
}
