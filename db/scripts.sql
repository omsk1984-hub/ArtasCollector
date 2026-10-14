-- ============================================================================
-- Сервисные запросы к public.fractals
-- ============================================================================
-- symbol_id: 1 = Si, 2 = CNY (контракт CRZ6), 3 = USDRUBF, 4 = CNYRUBF, 5 = IMOEXF
--
-- КАК ОТЛИЧИТЬ ТЕСТОВЫЕ ДАННЫЕ ОТ РЕАЛЬНЫХ:
--   Writer в режиме симуляции (`.\run_Writer.ps1 -Simulate` или
--   Finam__SimulationMode=true) помечает КАЖДУЮ записанную запись в колонке
--   `stat` значением "Fake" (JSONB-строка). Это основной и надёжный маркер.
--
--   Реализовано в FractalRepository: при SimulationMode=true в INSERT
--   вместо stat=NULL пишется stat='"Fake"'::jsonb.
--
--   Поэтому поиск и удаление тестовых данных сводятся к одной строке:
--     SELECT * FROM public.fractals WHERE stat = '"Fake"'::jsonb;
--     DELETE   FROM public.fractals WHERE stat = '"Fake"'::jsonb;
--
--   Дополнительно симулятор генерирует цены ~100..110 (старт цены =
--   100.0 + rand*10 в SimulationEventSource.RunAsync), поэтому price > 90 —
--   вспомогательный косвенный признак для данных, записанных ДО появления
--   маркера stat = "Fake".
-- ============================================================================


-- ----------------------------------------------------------------------------
-- 1) ОБЗОР: распределение цен и количества по символам
-- ----------------------------------------------------------------------------
SELECT
    symbol_id,
    COUNT(*)          AS cnt,
    MIN(price)        AS min_price,
    MAX(price)        AS max_price,
    MIN("time")       AS first_time,
    MAX("time")       AS last_time
FROM public.fractals
GROUP BY symbol_id
ORDER BY symbol_id;


-- ----------------------------------------------------------------------------
-- 2) ОСНОВНОЙ СПОСОБ: поиск тестовых данных по маркеру stat = "Fake"
--    (записывается автоматически при SimulationMode=true)
-- ----------------------------------------------------------------------------
SELECT id, symbol_id, price, "time", created_at, deleted
FROM public.fractals
WHERE stat = '"Fake"'::jsonb
ORDER BY "time" DESC;

-- Количество тестовых записей по символам:
SELECT symbol_id, COUNT(*) AS fake_cnt
FROM public.fractals
WHERE stat = '"Fake"'::jsonb
GROUP BY symbol_id
ORDER BY symbol_id;


-- ----------------------------------------------------------------------------
-- 2a) ВСПОМОГАТЕЛЬНЫЙ признак для записей, сделанных ДО появления маркера:
--     price > 90 (симулятор стартует с ~100)
-- ----------------------------------------------------------------------------
SELECT id, symbol_id, price, "time", created_at, deleted
FROM public.fractals
WHERE symbol_id = 3
  AND price > 90
ORDER BY "time" DESC;


-- ----------------------------------------------------------------------------
-- 3) ГРУППА тестовых данных одного прогона
--    Каждый запуск симулятора — это всплеск фракталов с id в узком
--    временном окне. Находим "пачки" по округлённому времени.
-- ----------------------------------------------------------------------------
SELECT
    date_trunc('minute', "time") AS run_minute,
    COUNT(*)                     AS fractals_in_run,
    MIN(price)                   AS min_price,
    MAX(price)                   AS max_price,
    MIN(id)                      AS min_id,
    MAX(id)                      AS max_id
FROM public.fractals
WHERE stat = '"Fake"'::jsonb
GROUP BY date_trunc('minute', "time")
ORDER BY run_minute DESC;


-- ----------------------------------------------------------------------------
-- 4) УДАЛЕНИЕ тестовых данных по маркеру
--    ВНИМАНИЕ: сначала выполните SELECT из блока 2,
--    убедитесь в списке, затем раскомментируйте DELETE.
-- ----------------------------------------------------------------------------
-- DELETE FROM public.fractals
-- WHERE stat = '"Fake"'::jsonb;

-- Мягкое удаление (если запись нужна для аудита, но не должна попадать
-- в выборки Reader/Stat -- они фильтруют deleted = false):
-- UPDATE public.fractals
-- SET deleted = true
-- WHERE stat = '"Fake"'::jsonb;


-- ----------------------------------------------------------------------------
-- 5) УДАЛЕНИЕ старого мусора, записанного ДО маркера (по цене)
-- ----------------------------------------------------------------------------
-- DELETE FROM public.fractals
-- WHERE symbol_id = 3
--   AND price > 90
--   AND stat IS NULL;

-- ----------------------------------------------------------------------------
-- 6) УДАЛЕНИЕ конкретного прогона по диапазону id
--    Возьмите min_id / max_id из блока 3 и подставьте:
-- ----------------------------------------------------------------------------
-- DELETE FROM public.fractals
-- WHERE id BETWEEN <min_id> AND <max_id>;


-- ----------------------------------------------------------------------------
-- 7) ИСПРАВЛЕНИЕ параметров символа Si (digits: 2 -> 0, point: 0.01 -> 1)
--    Применяется к уже созданной БД (init-скрипт 02 отрабатывает только
--    при первой инициализации).
-- ----------------------------------------------------------------------------
UPDATE public.symbols
SET digits = 0,
    point  = 1
WHERE id = 1;
-- Проверка:
-- SELECT id, name, symbol, description, digits, point FROM public.symbols WHERE id = 1;
