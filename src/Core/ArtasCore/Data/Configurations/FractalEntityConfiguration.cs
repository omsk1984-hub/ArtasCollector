using ArtasCore.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtasCore.Data.Configurations;

/// <summary>
/// Маппинг <see cref="FractalEntity"/> на таблицу <c>public.fractals</c>.
/// Таблица партиционирована по <c>symbol_id</c> (LIST partitioning) — секция
/// партиционирования и триггер автопартиций добавляются raw SQL в миграции.
/// </summary>
public sealed class FractalEntityConfiguration : IEntityTypeConfiguration<FractalEntity>
{
    public void Configure(EntityTypeBuilder<FractalEntity> builder)
    {
        builder.ToTable("fractals", "public");

        // Составной первичный ключ (id, symbol_id).
        builder.HasKey(f => new { f.Id, f.SymbolId });

        builder.Property(f => f.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(f => f.SymbolId)
            .HasColumnName("symbol_id")
            .IsRequired();

        builder.Property(f => f.Price).HasColumnName("price").IsRequired();

        builder.Property(f => f.Time)
            .HasColumnName("time")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(f => f.Metadata)
            .HasColumnName("metadata")
            .HasColumnType("jsonb");

        builder.Property(f => f.Stat)
            .HasColumnName("stat")
            .HasColumnType("jsonb")
            .HasComment("Статистические данные фрактала (произвольный JSON)");

        builder.Property(f => f.Deleted)
            .HasColumnName("deleted")
            .HasDefaultValue(false);

        builder.Property(f => f.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(f => f.Symbol)
            .WithMany()
            .HasForeignKey(f => f.SymbolId)
            .HasConstraintName("fractals_symbol_id_fkey")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(f => new { f.SymbolId, f.Time })
            .HasDatabaseName("idx_fractals_symbol_time")
            .IsDescending(false, true);

        builder.ToTable(t => t.HasComment(
            "Обнаруженные фракталы (технический анализ) с составным первичным ключом " +
            "(id, symbol_id), партиционированная по symbol_id"));
    }
}
