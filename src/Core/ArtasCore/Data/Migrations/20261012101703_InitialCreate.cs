using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ArtasCore.Data.Migrations
{
    /// <summary>
    /// Единая (объединённая) начальная миграция схемы Artas2026.
    /// Схлопывает прежние миграции InitialCreate, SeedInitialData,
    /// AddSymbolFilterSettings, AddCnyAndImoexf,
    /// AddCnyrubfParamsAndRenameSiPartition и AddMxMmRi в одну.
    /// Создаёт финальную схему «с нуля»: таблицы symbols, fractals
    /// (LIST-партиционированная по symbol_id), symbol_filter_settings, индексы,
    /// ограничения, DEFAULT-партицию и стартовый набор символов с партициями.
    /// </summary>
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

            // 1. Таблица символов.
            migrationBuilder.CreateTable(
                name: "symbols",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false, comment: "Постоянное уникальное имя (ключ поиска для сервисов)"),
                    symbol = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false, comment: "Реальное имя контракта для MT5 (может меняться, например SiZ6 -> SiH3)"),
                    description = table.Column<string>(type: "text", nullable: true),
                    digits = table.Column<int>(type: "integer", nullable: true),
                    point = table.Column<double>(type: "double precision", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_symbols", x => x.id);
                    table.UniqueConstraint("symbols_symbol_key", x => x.symbol);
                    table.UniqueConstraint("uq_symbols_name", x => x.name);
                },
                comment: "Валютные пары (символы) торгуемые в MetaTrader");

            // 2. Таблица fractals создаётся вручную (raw SQL), т.к. она объявлена
            // партиционированной по LIST (symbol_id) — EF Core не поддерживает
            // декларативное партиционирование PostgreSQL. Составной первичный
            // ключ (id, symbol_id) обязателен и включает ключ партиционирования.
            migrationBuilder.Sql("""
                CREATE TABLE public.fractals (
                    id BIGINT NOT NULL,
                    symbol_id INTEGER NOT NULL,
                    price DOUBLE PRECISION NOT NULL,
                    time TIMESTAMP WITH TIME ZONE NOT NULL,
                    metadata JSONB,
                    stat JSONB,
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                    deleted BOOLEAN DEFAULT false,
                    CONSTRAINT fractals_pkey1 PRIMARY KEY (id, symbol_id),
                    CONSTRAINT fractals_symbol_id_fkey FOREIGN KEY (symbol_id)
                        REFERENCES public.symbols(id) ON DELETE CASCADE
                ) PARTITION BY LIST (symbol_id);

                COMMENT ON TABLE public.fractals IS 'Обнаруженные фракталы (технический анализ) с составным первичным ключом (id, symbol_id), партиционированная по symbol_id';
                COMMENT ON COLUMN public.fractals.stat IS 'Статистические данные фрактала (произвольный JSON)';
                """);

            migrationBuilder.CreateIndex(
                name: "idx_fractals_symbol_time",
                schema: "public",
                table: "fractals",
                columns: new[] { "symbol_id", "time" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_symbols_name",
                schema: "public",
                table: "symbols",
                column: "name");

            // DEFAULT-партиция таблицы fractals — ловушка для неизвестных символов.
            // Партиции под конкретные символы создаются ниже, после вставки символов
            // (индекс idx_fractals_symbol_time создан на родителе и автоматически
            // наследуется всеми партициями).
            migrationBuilder.Sql("""
                CREATE TABLE public.fractals_default PARTITION OF public.fractals DEFAULT;
                """);

            // 3. Таблица пользовательских настроек фильтра фракталов по символам.
            migrationBuilder.CreateTable(
                name: "symbol_filter_settings",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    symbol_id = table.Column<int>(type: "integer", nullable: false),
                    settings = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb", comment: "Настройки фильтра фракталов символа (JSONB)"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_symbol_filter_settings", x => x.id);
                    table.ForeignKey(
                        name: "symbol_filter_settings_symbol_id_fkey",
                        column: x => x.symbol_id,
                        principalSchema: "public",
                        principalTable: "symbols",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Пользовательские настройки фильтра фракталов по символам");

            migrationBuilder.CreateIndex(
                name: "uq_symbol_filter_settings_symbol_id",
                schema: "public",
                table: "symbol_filter_settings",
                column: "symbol_id",
                unique: true);

            // 4. Стартовый набор символов с фиксированными id (детерминированные
            // диапазоны партиций). name — постоянный ключ поиска, symbol — код
            // контракта для Finam. После вставки последовательность symbols_id_seq
            // подтягивается до MAX(id), чтобы автоинкрементные вставки не конфликтовали.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    INSERT INTO public.symbols (id, name, symbol, description, digits, point) VALUES
                        (1, 'Si',      'SiZ6',      'US Dollar vs Russian Rubble',          2, 0.01),
                        (2, 'CNY',     'CRZ6',      'Chinese Yuan vs Russian Rubble Futures', 3, 0.001),
                        (3, 'USDRUBF', 'USDRUBF',   'US Dollar vs Russian Rubble Futures',   2, 0.01),
                        (4, 'CNYRUBF', 'CNYRUBF',   'Chinese Yuan vs Russian Rubble Futures', 3, 0.001),
                        (5, 'IMOEXF',  'IMOEXF',    'MOEX',                                  1, 0.5),
                        (6, 'MX',      'MXZ6',      'MOEX max',                              0, 25),
                        (7, 'MM',      'MMZ6',      'MOEX mini',                             2, 0.05),
                        (8, 'RI',      'RIZ6',      'RTS',                                   0, 10)
                    ON CONFLICT (name) DO NOTHING;

                    PERFORM setval(
                        pg_get_serial_sequence('public.symbols', 'id'),
                        GREATEST((SELECT MAX(id) FROM public.symbols), 8),
                        true);
                END $$;
                """);

            // 5. Партиции таблицы fractals под каждый созданный символ.
            // Имя партиции строится по name символа (постоянный ключ поиска),
            // а не по коду контракта symbol. Индекс idx_fractals_symbol_time
            // унаследован от родительской таблицы; каждой партиции дополнительно
            // задаётся индекс по (time DESC).
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    sym RECORD;
                    partition_name TEXT;
                BEGIN
                    FOR sym IN SELECT id, name FROM public.symbols ORDER BY id LOOP
                        partition_name := 'fractals_' || LOWER(sym.name);

                        IF NOT EXISTS (
                            SELECT 1 FROM pg_class c
                            JOIN pg_namespace n ON n.oid = c.relnamespace
                            WHERE c.relname = partition_name AND n.nspname = 'public'
                        ) THEN
                            EXECUTE format(
                                'CREATE TABLE public.%I PARTITION OF public.fractals FOR VALUES IN (%s)',
                                partition_name, sym.id);

                            EXECUTE format(
                                'CREATE INDEX IF NOT EXISTS idx_%s_time ON public.%I(time DESC)',
                                partition_name, partition_name);
                        END IF;
                    END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Партиционированная таблица fractals удаляется вместе с партициями.
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.fractals CASCADE;");

            migrationBuilder.DropTable(
                name: "symbol_filter_settings",
                schema: "public");

            migrationBuilder.DropTable(
                name: "symbols",
                schema: "public");
        }
    }
}
