namespace ArtasTools.MemReport.Options;

/// <summary>
/// Параметры формирования отчёта по утечке памяти.
/// </summary>
public sealed class ReportOptions
{
    /// <summary>Базовый URL Prometheus API (порт проброшен из WSL).</summary>
    public string PrometheusUrl { get; set; } = "http://localhost:9090";

    /// <summary>Глубина окна анализа в часах.</summary>
    public int Hours { get; set; } = 12;

    /// <summary>Шаг выборки для range-запросов (Prometheus duration).</summary>
    public string Step { get; set; } = "60s";

    /// <summary>
    /// Минимальное число точек серии, чтобы тренд считался достоверным.
    /// Короткие серии (стартовый прогрев перезапущенного процесса) исключаются
    /// из вердикта.
    /// </summary>
    public int MinSamples { get; set; } = 30;

    /// <summary>
    /// Минимальная длительность наблюдения серии (минуты), чтобы тренду доверять.
    /// </summary>
    public int MinDurationMinutes { get; set; } = 60;

    /// <summary>Каталог для отчётов. Пусто — <корень>/reports.</summary>
    public string OutputDir { get; set; } = "";

    /// <summary>Фильтр по метке service_name. Пусто — не фильтровать.</summary>
    public string Job { get; set; } = "artas-writer";

    /// <summary>Форматы вывода.</summary>
    public OutputFormat Format { get; set; } = OutputFormat.Both;

    /// <summary>Дополнительно печатать Markdown в консоль.</summary>
    public bool PrintToConsole { get; set; }
}

/// <summary>Форматы вывода отчёта.</summary>
public enum OutputFormat
{
    Markdown,
    Json,
    Both
}
