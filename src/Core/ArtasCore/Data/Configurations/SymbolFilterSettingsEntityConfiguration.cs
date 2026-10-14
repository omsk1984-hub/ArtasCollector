using ArtasCore.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtasCore.Data.Configurations;

/// <summary>
/// Маппинг <see cref="SymbolFilterSettingsEntity"/> на таблицу
/// <c>public.symbol_filter_settings</c>. На каждый символ — не более одной строки
/// (уникальный индекс по <c>symbol_id</c>), что позволяет выполнять upsert
/// через <c>ON CONFLICT (symbol_id) DO UPDATE</c>.
/// </summary>
public sealed class SymbolFilterSettingsEntityConfiguration
    : IEntityTypeConfiguration<SymbolFilterSettingsEntity>
{
    public void Configure(EntityTypeBuilder<SymbolFilterSettingsEntity> builder)
    {
        builder.ToTable("symbol_filter_settings", "public");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(s => s.SymbolId)
            .HasColumnName("symbol_id")
            .IsRequired();

        builder.Property(s => s.Settings)
            .HasColumnName("settings")
            .HasColumnType("jsonb")
            .IsRequired()
            .HasDefaultValueSql("'{}'::jsonb");

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(s => s.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        // Уникальность по символу — одна запись настроек на символ.
        builder.HasIndex(s => s.SymbolId)
            .IsUnique()
            .HasDatabaseName("uq_symbol_filter_settings_symbol_id");

        // Каскадное удаление вместе с символом.
        builder.HasOne<SymbolEntity>()
            .WithMany()
            .HasForeignKey(s => s.SymbolId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("symbol_filter_settings_symbol_id_fkey");

        builder.Property(s => s.Settings)
            .HasComment("Настройки фильтра фракталов символа (JSONB)");

        builder.ToTable(t => t.HasComment("Пользовательские настройки фильтра фракталов по символам"));
    }
}
