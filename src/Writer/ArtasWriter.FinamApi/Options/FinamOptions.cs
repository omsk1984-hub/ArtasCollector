namespace ArtasWriter.FinamApi.Options;

/// <summary>
/// Настройки подключения к Finam API (секция <c>Finam</c> в appsettings.json).
/// </summary>
public sealed class FinamOptions
{
    public const string SectionName = "Finam";

    /// <summary>Базовый URL REST API.</summary>
    public string BaseUrl { get; set; } = "https://api.finam.ru";

    /// <summary>URL gRPC-эндпоинта (основной транспорт приёма тиков).</summary>
    public string GrpcUrl { get; set; } = "https://api.finam.ru";

    /// <summary>URL WebSocket-эндпоинта (альтернативный транспорт).</summary>
    public string WsUrl { get; set; } = "wss://api.finam.ru/ws";

    /// <summary>
    /// Секретный ключ Finam (secret key, <c>tapi_sk_...</c>). Не является bearer-токеном:
    /// обменивается на JWT через gRPC <c>AuthService.Auth</c>.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Транспорт: "gRPC" (по умолчанию) или "WebSocket".</summary>
    public string Transport { get; set; } = "gRPC";

    /// <summary>
    /// MIC биржи для формирования полного символа <c>{ticker}@{mic}</c>
    /// при подписке. Для инструментов MOEX FORTS (фьючерсы) — <c>RTSX</c>,
    /// для акций MOEX — <c>MISX</c>.
    /// </summary>
    public string DefaultMic { get; set; } = "RTSX";

    /// <summary>Задержка переподключения (мс).</summary>
    public int ReconnectDelayMs { get; set; } = 5000;

    /// <summary>
    /// Максимальное число последовательных неудачных попыток переподключения
    /// одного стрима (котировки / стакан / сделки). По исчерпании лимита
    /// проблемный стрим останавливается, остальные продолжают работать.
    /// </summary>
    public int StreamMaxRetries { get; set; } = 3;

    /// <summary>
    /// Пробовать повторную авторизацию (Auth) при сбое стрима.
    /// При успешном Auth обновляется общий JWT (счётчик попыток стрима не сбрасывается).
    /// </summary>
    public bool ReauthOnRetry { get; set; } = true;

    /// <summary>
    /// Период планового обновления JWT фоновым auth-keepalive (минуты).
    /// Дополнительно JWT обновляется немедленно по сигналу стрима (Unauthenticated).
    /// </summary>
    public int AuthRefreshMinutes { get; set; } = 50;

    /// <summary>
    /// Режим имитации: если true — продьюсер генерирует синтетические события
    /// (для тестов и разработки без реального подключения к Finam).
    /// </summary>
    public bool SimulationMode { get; set; }

    /// <summary>
    /// Сформировать полный символ Finam в виде <c>ticker@mic</c>, если MIC
    /// ещё не указан в символе.
    /// </summary>
    public string BuildFullSymbol(string ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker))
        {
            return ticker;
        }

        if (ticker.Contains('@', StringComparison.Ordinal))
        {
            return ticker;
        }

        return string.IsNullOrWhiteSpace(DefaultMic)
            ? ticker
            : $"{ticker}@{DefaultMic}";
    }
}
