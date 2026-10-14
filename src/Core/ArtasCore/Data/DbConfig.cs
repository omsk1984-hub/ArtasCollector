using Microsoft.Extensions.Configuration;

namespace ArtasCore.Data;

/// <summary>
/// Конфигурация подключения к PostgreSQL. Аналог Python
/// <c>artas2026.db.config.DbConfig</c>.
/// </summary>
public sealed class DbConfig
{
    // Явный IPv4 вместо "localhost": .NET-резолвер на Windows возвращает ::1,
    // а PostgreSQL в WSL слушает только IPv4 127.0.0.1.
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 5432;
    public string Database { get; init; } = "artas_db";
    public string User { get; init; } = "artas_user";
    public string Password { get; init; } = "artas_password";

    /// <summary>Строка подключения Npgsql.</summary>
    public string ConnectionString =>
        $"Host={Host};Port={Port};Database={Database};Username={User};Password={Password};" +
        "Timezone=UTC;Pooling=true;Minimum Pool Size=0;Maximum Pool Size=10;" +
        "Timeout=5;Command Timeout=15";

    /// <summary>
    /// Построить конфигурацию из IConfiguration.
    /// Ключи поддерживают вложенную форму (<c>Db:Host</c>) и плоскую (<c>DB_HOST</c>).
    /// </summary>
    public static DbConfig FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new DbConfig
        {
            Host = configuration["Db:Host"] ?? configuration["DB_HOST"] ?? "127.0.0.1",
            Port = ParseInt(configuration["Db:Port"] ?? configuration["DB_PORT"], 5432),
            Database = configuration["Db:Database"] ?? configuration["DB_NAME"] ?? "artas_db",
            User = configuration["Db:User"] ?? configuration["DB_USER"] ?? "artas_user",
            Password = configuration["Db:Password"] ?? configuration["DB_PASSWORD"] ?? "artas_password",
        };
    }

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) ? parsed : fallback;

    /// <summary>
    /// Конфигурация по умолчанию (как в Python-проекте).
    /// </summary>
    public static DbConfig Default => new();

    public override string ToString() => $"{User}@{Host}:{Port}/{Database}";
}
