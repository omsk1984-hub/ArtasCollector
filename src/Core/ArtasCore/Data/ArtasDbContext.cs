using ArtasCore.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ArtasCore.Data;

/// <summary>
/// EF Core <see cref="DbContext"/> для схемы БД Artas2026.
/// Используется исключительно для генерации и применения миграций
/// (<c>dotnet ef</c>); рантайм-доступ к данным приложений остаётся на Npgsql.
/// </summary>
public sealed class ArtasDbContext : DbContext
{
    /// <summary>Имя таблицы истории миграций EF (для baseline существующих БД).</summary>
    public const string MigrationsHistoryTable = "__EFMigrationsHistory";

    /// <summary>Дополнительная схема по умолчанию для всех сущностей.</summary>
    public const string DefaultSchema = "public";

    public ArtasDbContext(DbContextOptions<ArtasDbContext> options)
        : base(options)
    {
    }

    /// <summary>Символы (валютные пары).</summary>
    public DbSet<SymbolEntity> Symbols => Set<SymbolEntity>();

    /// <summary>Фракталы.</summary>
    public DbSet<FractalEntity> Fractals => Set<FractalEntity>();

    /// <summary>Настройки фильтра фракталов по символам.</summary>
    public DbSet<SymbolFilterSettingsEntity> SymbolFilterSettings => Set<SymbolFilterSettingsEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Все конфигурации (IEntityTypeConfiguration) из сборки ArtasCore.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ArtasDbContext).Assembly);
    }
}
