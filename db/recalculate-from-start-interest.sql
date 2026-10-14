-- =============================================================================
-- Пересчёт metadata.from_start_interest для ВСЕХ инструментов (реальные данные).
-- =============================================================================
-- Правила проекта (прямое соответствие коду):
--
--   1. База дня = интерес ('metadata'->'peak'->>'interest') САМОГО РАННЕГО
--      боевого фрактала календарного дня (МСК).
--      Источник: FractalRepository.GetDayStartInterestAsync()
--      (WHERE deleted = false AND stat IS NULL ORDER BY time ASC LIMIT 1).
--
--   2. from_start_interest = interest фрактала - база дня.
--      Источник: FractalBufferService.AddFractal()
--      (fractal.FromStartInterest = fractal.Interest - _dayStartInterest).
--
--   3. День определяется по UTC-дате пикового времени (TickPeak.TimeUtc).
--      Вся логика проекта ведётся по UTC; МСК (UTC+3) — только для визуализации.
--      Границы дня совпадают с UTC-датой хранения ("time").
--      Источник: MyTick.TimeUtc, FractalBufferService.AddFractal().
--
--   4. База считается ОТДЕЛЬНО по каждому инструменту (symbol_id) и каждому дню.
--
-- ВАЖНО:
--   * Обновляются только боевые записи: stat IS NULL (симуляционные, 'Fake',
--     исключены).
--   * UTC-день вычисляется как ("time" AT TIME ZONE 'UTC')::date — без сдвига
--     на МСК. Это соответствует определению дня по TimeUtc в коде Writer.
--   * "Самый ранний" задан по id ASC (id фракталов монотонно растёт со временем).
--     Если это не так в вашем наборе, замените ORDER BY на ... "time" ASC.
-- =============================================================================

-- -----------------------------------------------------------------------------
-- ШАГ 1. ПРОВЕРКА (dry-run): показать строки, где значение изменится.
--         Данные НЕ изменяются. Выполните перед UPDATE.
-- -----------------------------------------------------------------------------
WITH real_data AS (
    SELECT
        symbol_id,
        id,
        -- Календарный день по UTC (границы дня совпадают с UTC-датой хранения).
        ("time" AT TIME ZONE 'UTC')::date AS utc_day,
        -- Интерес пика фрактала.
        (metadata->'peak'->>'interest')::bigint AS interest,
        -- Текущее значение (может отсутствовать).
        NULLIF(metadata->>'from_start_interest', '')::bigint AS current_fsi
    FROM public.fractals
    WHERE stat IS NULL            -- только боевые (реальные) записи
      AND deleted = false         -- исключаем помеченные на удаление
      AND metadata->'peak'->>'interest' IS NOT NULL
),
day_base AS (
    -- База дня = интерес самого раннего фрактала инструмента в этот день.
    SELECT DISTINCT ON (symbol_id, utc_day)
        symbol_id,
        utc_day,
        interest AS day_base_interest
    FROM real_data
    ORDER BY symbol_id, utc_day, id ASC
)
SELECT
    rd.symbol_id,
    rd.utc_day,
    rd.id,
    rd.current_fsi,
    db.day_base_interest,
    rd.interest,
    rd.interest - db.day_base_interest AS expected_fsi
FROM real_data rd
JOIN day_base db
  ON db.symbol_id = rd.symbol_id
 AND db.utc_day = rd.utc_day
WHERE rd.current_fsi IS DISTINCT FROM (rd.interest - db.day_base_interest)
ORDER BY rd.symbol_id, rd.utc_day, rd.id;

-- -----------------------------------------------------------------------------
-- ШАГ 2. ОБНОВЛЕНИЕ. Выполняется в транзакции для возможности отката.
--         При неверном результате выполните ROLLBACK вместо COMMIT.
-- -----------------------------------------------------------------------------
BEGIN;

WITH real_data AS (
    SELECT
        symbol_id,
        id,
        ("time" AT TIME ZONE 'UTC')::date AS utc_day,
        (metadata->'peak'->>'interest')::bigint AS interest
    FROM public.fractals
    WHERE stat IS NULL
      AND deleted = false
      AND metadata->'peak'->>'interest' IS NOT NULL
),
day_base AS (
    SELECT DISTINCT ON (symbol_id, utc_day)
        symbol_id,
        utc_day,
        interest AS day_base_interest
    FROM real_data
    ORDER BY symbol_id, utc_day, id ASC
)
UPDATE public.fractals f
SET metadata = jsonb_set(
        f.metadata,
        '{from_start_interest}',
        to_jsonb(rd.interest - db.day_base_interest),
        true)
FROM real_data rd
JOIN day_base db
  ON db.symbol_id = rd.symbol_id
 AND db.utc_day = rd.utc_day
WHERE f.symbol_id = rd.symbol_id
  AND f.id = rd.id;

COMMIT;
-- При необходимости отката замените COMMIT на ROLLBACK.

-- =============================================================================
-- ПРИЛОЖЕНИЕ. Разовая правка по времени (пример из задачи).
-- =============================================================================
-- Обнулить from_start_interest только у РЕАЛЬНЫХ фракталов в диапазоне
-- 2026-10-12 13:31:32.512 MSK ... 2026-10-12 13:34:42.025 MSK.
--
-- UPDATE public.fractals
-- SET metadata = jsonb_set(metadata, '{from_start_interest}', '0'::jsonb, true)
-- WHERE stat IS NULL
--   AND "time" BETWEEN '2026-10-12 13:31:32.512+03'::timestamptz
--                  AND '2026-10-12 13:34:42.025+03'::timestamptz;
