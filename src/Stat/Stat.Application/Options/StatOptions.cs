namespace Stat.Application.Options;

/// <summary>
/// Настройки batch-сервиса статистики (секция <c>Stat</c> в appsettings.json
/// либо аргументы командной строки).
/// </summary>
public sealed class StatOptions
{
    /// <summary>Имя секции конфигурации.</summary>
    public const string SectionName = "Stat";

    /// <summary>Постоянное имя символа (совпадает с <c>public.symbols.name</c>).</summary>
    public string Symbol { get; set; } = "CNYRUBF";

    /// <summary>Размер стопа в пунктах.</summary>
    public int StopPoint { get; set; } = 3;

    /// <summary>Размер тейка в пунктах.</summary>
    public int TakePoint { get; set; } = 20;

    /// <summary>Глубина просмотра вперёд для расчёта статистики (в Python — <c>max_deep=100</c>).</summary>
    public int MaxDeep { get; set; } = 100;

    /// <summary>Размер циклического буфера фракталов.</summary>
    public int BufferSize { get; set; } = 500;

    /// <summary>Количество фракталов, читаемых из БД за одну итерацию (в Python — <c>limit=500</c>).</summary>
    public int ReadChunkSize { get; set; } = 500;

    /// <summary>Очищать статистику перед прогоном (<c>UPDATE fractals SET stat = null</c>).</summary>
    public bool Clear { get; set; }

    /// <summary>Размер батча записи статистики в БД.</summary>
    public int WriteBatchSize { get; set; } = 200;
}
