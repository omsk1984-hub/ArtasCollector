using ArtasCore.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtasCore.Data.Configurations;

/// <summary>
/// Маппинг <see cref="SymbolEntity"/> на таблицу <c>public.symbols</c>.
/// </summary>
public sealed class SymbolEntityConfiguration : IEntityTypeConfiguration<SymbolEntity>
{
    public void Configure(EntityTypeBuilder<SymbolEntity> builder)
    {
        builder.ToTable("symbols", "public");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(s => s.Symbol)
            .HasColumnName("symbol")
            .HasMaxLength(12)
            .IsRequired();

        builder.Property(s => s.Description).HasColumnName("description");

        builder.Property(s => s.Digits).HasColumnName("digits");

        builder.Property(s => s.Point).HasColumnName("point");

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(s => s.Name)
            .HasColumnName("name")
            .HasMaxLength(12)
            .IsRequired();

        // Уникальные ограничения (UNIQUE CONSTRAINT, а не просто UNIQUE INDEX) —
        // как в фактической схеме БД: symbols_symbol_key и uq_symbols_name.
        // Alternate key создаёт именно UNIQUE CONSTRAINT, что важно для
        // ON CONFLICT (...) в seed-миграции.
        builder.HasAlternateKey(s => s.Symbol).HasName("symbols_symbol_key");
        builder.HasAlternateKey(s => s.Name).HasName("uq_symbols_name");

        // Дополнительный неуникальный индекс поиска по name (idx_symbols_name).
        builder.HasIndex(s => s.Name).HasDatabaseName("idx_symbols_name");

        builder.Property(s => s.Name).HasComment("Постоянное уникальное имя (ключ поиска для сервисов)");
        builder.Property(s => s.Symbol).HasComment(
            "Реальное имя контракта для MT5 (может меняться, например SiZ6 -> SiH3)");
        builder.ToTable(t => t.HasComment("Валютные пары (символы) торгуемые в MetaTrader"));
    }
}
