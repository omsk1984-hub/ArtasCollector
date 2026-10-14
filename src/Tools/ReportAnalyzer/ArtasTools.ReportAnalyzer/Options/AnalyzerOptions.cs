namespace ArtasTools.ReportAnalyzer.Options;

/// <summary>Параметры анализа каталога с отчётами MemReport.</summary>
public sealed class AnalyzerOptions
{
    /// <summary>Каталог с отчётами (*.json / *.md). Пусто — <корень>/reports.</summary>
    public string ReportsDir { get; set; } = "";

    /// <summary>Каталог для сводных отчётов анализа. Пусто — <корень>/reports.</summary>
    public string OutputDir { get; set; } = "";

    /// <summary>Анализировать только отчёты не старше N часов (0 — без ограничения).</summary>
    public int LastHours { get; set; }

    /// <summary>Форматы вывода.</summary>
    public OutputFormat Format { get; set; } = OutputFormat.Both;

    /// <summary>Дополнительно печатать Markdown-сводку в консоль.</summary>
    public bool PrintToConsole { get; set; }

    /// <summary>Порог «шумного» фита: R² ниже — тренд не считается достоверным.</summary>
    public double MinR2 { get; set; } = 0.5;

    /// <summary>
    /// Минимальное число точек серии, чтобы тренд считался достоверным.
    /// Короткие стартовые серии (перезапуск процесса) исключаются из эскалации.
    /// </summary>
    public int MinSamples { get; set; } = 30;

    /// <summary>Минимальная длительность наблюдения серии (минуты).</summary>
    public int MinDurationMinutes { get; set; } = 60;

    /// <summary>Порог устойчивого роста Gen2/LOH heap, МБ/час (WARNING).</summary>
    public double Gen2WarnSlope { get; set; } = 5.0;

    /// <summary>Порог критичного роста Gen2/LOH heap, МБ/час (CRITICAL).</summary>
    public double Gen2CriticalSlope { get; set; } = 40.0;

    /// <summary>Порог устойчивого роста heap, МБ/час (WARNING).</summary>
    public double HeapWarnSlope { get; set; } = 10.0;

    /// <summary>Порог критичного роста heap, МБ/час (CRITICAL).</summary>
    public double HeapCriticalSlope { get; set; } = 50.0;

    /// <summary>Порог устойчивого роста RSS, МБ/час (WARNING).</summary>
    public double RssWarnSlope { get; set; } = 20.0;

    /// <summary>Порог критичного роста RSS, МБ/час (CRITICAL).</summary>
    public double RssCriticalSlope { get; set; } = 100.0;
}

/// <summary>Форматы вывода сводного отчёта анализа.</summary>
public enum OutputFormat
{
    Markdown,
    Json,
    Both
}
