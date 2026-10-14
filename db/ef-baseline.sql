-- Baseline для УЖЕ существующей БД artas_db, созданной прежними SQL-скриптами
-- из db/init-scripts/. Помечает EF-миграции как применённые, НЕ выполняя их DDL.
--
-- Применять ТОЛЬКО к базе, схема которой уже соответствует целевой
-- (таблицы symbols/fractals/symbol_filter_settings + партиции + данные).
-- Для чистой БД этот скрипт не нужен: достаточно `dotnet ef database update`.
--
-- Использование:
--   psql "host=127.0.0.1 port=5432 dbname=artas_db user=artas_user password=..." -f ef-baseline.sql

BEGIN;

-- Таблица истории миграций EF.
CREATE TABLE IF NOT EXISTS public."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

-- Помечаем единственную (объединённую) миграцию как применённую.
-- Значение совпадает с именем файла миграции в
-- src/Core/ArtasCore/Data/Migrations.
INSERT INTO public."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES
    ('20261012101703_InitialCreate', '8.0.11')
ON CONFLICT ("MigrationId") DO NOTHING;

COMMIT;
