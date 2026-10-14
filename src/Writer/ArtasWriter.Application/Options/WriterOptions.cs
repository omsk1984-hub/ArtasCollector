namespace ArtasWriter.Application.Options;

/// <summary>
/// Настройка одного инструмента: имя символа, по которому ищем в <c>public.symbols.name</c>.
/// </summary>
public sealed class SymbolSetting
{
    /// <summary>
    /// Имя инструмента (совпадает с <c>public.symbols.name</c>, например "Si", "CNYRUBF").
    /// Код для соединения с Finam берётся из <c>public.symbols.symbol</c>.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Настройки сервиса записи фракталов (секция <c>Writer</c> в appsettings.json).
/// </summary>
public sealed class WriterOptions
{
    public const string SectionName = "Writer";

    /// <summary>Список инструментов (все обрабатываются одним процессом).</summary>
    public List<SymbolSetting> Symbols { get; set; } = new();

    /// <summary>Порт health-эндпоинтов.</summary>
    public int HealthPort { get; set; } = 8080;

    /// <summary>
    /// Емкость канала событий продьюсера Finam (на каждый инструмент).
    /// При переполнении вытесняются самые старые события (DropOldest).
    /// </summary>
    public int ProducerChannelCapacity { get; set; } = 10_000;

    /// <summary>
    /// Емкость общего канала записи фракталов в БД (один на процесс).
    /// При переполнении продьюсеры блокируются (backpressure).
    /// </summary>
    public int WriterChannelCapacity { get; set; } = 10_000;

    /// <summary>Размер батча записи фракталов в PG.</summary>
    public int WriteBatchSize { get; set; } = 100;

    /// <summary>Интервал сброса буфера записи (мс).</summary>
    public int WriteFlushIntervalMs { get; set; } = 1000;

    /// <summary>Размер буфера фракталов на инструмент.</summary>
    public int FractalBufferSize { get; set; } = 500;
}
