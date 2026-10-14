using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ArtasCore.Data;

/// <summary>
/// Design-time фабрика <see cref="ArtasDbContext"/> для команд <c>dotnet ef</c>.
/// Строка подключения берётся из <see cref="DbConfig"/> (по умолчанию —
/// <c>127.0.0.1:5432/artas_db</c>) или переопределяется переменной окружения
/// <c>ARTAS_MIGRATIONS_CONNECTION</c>. Само соединение нужно только чтобы
/// определить провайдер; для генерации миграций база не требуется.
/// </summary>
public sealed class ArtasDbContextFactory : IDesignTimeDbContextFactory<ArtasDbContext>
{
    public ArtasDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ARTAS_MIGRATIONS_CONNECTION");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = DbConfig.Default.ConnectionString;
        }

        var options = new DbContextOptionsBuilder<ArtasDbContext>()
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable(ArtasDbContext.MigrationsHistoryTable);
                npgsql.MigrationsAssembly(typeof(ArtasDbContext).Assembly.GetName().Name);
            })
            .Options;

        return new ArtasDbContext(options);
    }
}
