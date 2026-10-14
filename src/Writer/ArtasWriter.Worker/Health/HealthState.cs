using System.Text.Json.Serialization;

namespace ArtasWriter.Worker.Health;

/// <summary>Данные последнего обработанного тика в health-ответе.</summary>
public sealed class LastTickHealth
{
    /// <summary>Последняя цена сделки.</summary>
    [JsonPropertyName("last")] public double Last { get; set; }

    /// <summary>Объём последнего тика.</summary>
    [JsonPropertyName("volume")] public long Volume { get; set; }

    /// <summary>Время тика в миллисекундах (UTC).</summary>
    [JsonPropertyName("time_msc")] public long TimeMsc { get; set; }

    /// <summary>Время тика в московском времени (MSK, UTC+3).</summary>
    [JsonPropertyName("time_msk")] public DateTime TimeMsk { get; set; }
}

/// <summary>Один уровень стакана цен в health-ответе.</summary>
public sealed class BookLevelHealth
{
    /// <summary>Цена уровня.</summary>
    [JsonPropertyName("price")] public double Price { get; set; }

    /// <summary>Объём уровня.</summary>
    [JsonPropertyName("volume")] public int Volume { get; set; }
}

/// <summary>Стакан цен: лучший Ask и лучший Bid из середины стакана.</summary>
public sealed class BookHealth
{
    /// <summary>Лучший Ask (цена продажи, слева от центра).</summary>
    [JsonPropertyName("ask")] public BookLevelHealth? Ask { get; set; }

    /// <summary>Лучший Bid (цена покупки, справа от центра).</summary>
    [JsonPropertyName("bid")] public BookLevelHealth? Bid { get; set; }
}

/// <summary>Состояние одного инструмента в health-ответе.</summary>
public sealed class SymbolHealth
{
    /// <summary>
    /// Состояние соединения Finam для данного символа.
    /// false — если хотя бы один стрим символа исчерпал ретраи.
    /// Выводится ПЕРВЫМ полем блока символа.
    /// </summary>
    [JsonPropertyName("finam_connected")] public bool FinamConnected { get; set; } = true;

    [JsonPropertyName("symbol")] public string Symbol { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("last_tick")] public LastTickHealth? LastTick { get; set; }
    [JsonPropertyName("book")] public BookHealth? Book { get; set; }
    [JsonPropertyName("open_interest")] public long OpenInterest { get; set; }
    [JsonPropertyName("fractals_count_today")] public int FractalsCountToday { get; set; }
    [JsonPropertyName("processed_ticks")] public long ProcessedTicks { get; set; }
    [JsonPropertyName("written_fractals")] public long WrittenFractals { get; set; }
}

/// <summary>Полный снапшот health-статуса сервиса.</summary>
public sealed class HealthSnapshot
{
    [JsonPropertyName("status")] public string Status { get; set; } = "ok";
    [JsonPropertyName("server_time")] public DateTime ServerTime { get; set; }
    [JsonPropertyName("start_time")] public DateTime StartTime { get; set; }
    [JsonPropertyName("uptime_seconds")] public long UptimeSeconds { get; set; }
    [JsonPropertyName("finam_connected")] public bool FinamConnected { get; set; }
    [JsonPropertyName("db_connected")] public bool DbConnected { get; set; }
    [JsonPropertyName("total_written")] public long TotalWritten { get; set; }
    [JsonPropertyName("symbols")] public List<SymbolHealth> Symbols { get; set; } = new();
}
