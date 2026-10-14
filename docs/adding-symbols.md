# Правило добавления нового символа (инструмента)

> Относится к БД Artas2026 и сервису `ArtasWriter`.
> Все пути — относительно корня репозитория `d:/work/Artas2026`.

Символы (инструменты) хранятся в таблице [`public.symbols`](../db/database_schema.md)
и обрабатываются сервисом `ArtasWriter`. Добавление символа — это **три
обязательных шага**: запись в БД (миграция + партиция), регистрация в конфиге
Writer и проверка. Всё, что делает код, уже читает символы динамически
(Reader — из БД, Writer — из `appsettings.json`), поэтому правок в C#-коде не
требуется.

---

## 0. Предварительно: определите параметры символа

| Поле | Смысл | Пример |
|------|-------|--------|
| `id` | Первичный ключ. Берите следующий свободный (`MAX(id) + 1`). Для новых символов фиксируйте явно, чтобы партиция имела детерминированный диапазон. | `6` |
| `name` | **Постоянный** ключ поиска (не меняется при смене контракта). Совпадает с `Writer:Symbols[].Name`. Из него строится имя партиции. | `MX` |
| `symbol` | Код **текущего контракта** для Finam (может меняться, например `MXZ6` → `MXH7`). | `MXZ6` |
| `description` | Человекочитаемое описание. | `MOEX max` |
| `digits` | Количество знаков после запятой (для форматирования/отображения). | `0` |
| `point` | Минимальный шаг цены (`trade_tick_size`). | `25` |

> **Важно:** имя партиции строится по `name` в нижнем регистре — `name='MX'` →
> партиция `fractals_mx`. Партиция должна существовать **до** записи фракталов,
> иначе данные попадут в `fractals_default`.

---

## 1. Создайте EF Core миграцию

Миграции лежат в [`src/Core/ArtasCore/Data/Migrations/`](../src/Core/ArtasCore/Data/Migrations).
Прежние миграции схлопнуты в единую [`InitialCreate`](../src/Core/ArtasCore/Data/Migrations/20261012101703_InitialCreate.cs),
которая создаёт финальную схему «с нуля» (таблицы, символы с фиксированными `id`
и партиции). Новую миграцию создавайте поверх неё — она должна содержать три операции:

### 1.1. INSERT символа + подтяжка последовательности

> **Важно:** `migrationBuilder.Sql()` выполняет **чистый SQL**, а не PL/pgSQL.
> Поэтому `PERFORM` (это PL/pgSQL) нужно оборачивать в блок `DO $$ ... $$`.
> Без обёртки PostgreSQL вернёт `42601: syntax error at or near "PERFORM"`.

```sql
DO $$
BEGIN
    INSERT INTO public.symbols (id, name, symbol, description, digits, point) VALUES
        (6, 'MX', 'MXZ6', 'MOEX max', 0, 25)
    ON CONFLICT (name) DO NOTHING;

    -- Последовательность symbols_id_seq подтягивается до MAX(id),
    -- иначе следующие автоинкрементные вставки конфликтуют с фиксированными id.
    PERFORM setval(
        pg_get_serial_sequence('public.symbols', 'id'),
        GREATEST((SELECT MAX(id) FROM public.symbols), 6),
        true);
END $$;
```

### 1.2. Создание партиции `fractals_<name>` + индекса

Выполняется в блоке `DO $$` (идемпотентно, с проверкой существования):

```sql
DO $$
DECLARE
    sym RECORD;
    partition_name TEXT;
BEGIN
    FOR sym IN
        SELECT id, name FROM public.symbols
        WHERE name IN ('MX')          -- перечислите новые name
        ORDER BY id
    LOOP
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
```

### 1.3. Down-миграция

```sql
DROP TABLE IF EXISTS public.fractals_mx;
DELETE FROM public.symbols WHERE id IN (6);
```

### 1.4. Designer-файл и снапшот

EF Core требует парный `<timestamp>_<Name>.Designer.cs` и обновлённый
[`ArtasDbContextModelSnapshot.cs`](../src/Core/ArtasCore/Data/Migrations/ArtasDbContextModelSnapshot.cs).
Их проще всего сгенерировать командой (в проекте `ArtasCore`):

```powershell
dotnet ef migrations add AddNewSymbol `
    --project src/Core/ArtasCore/ArtasCore.csproj `
    --startup-project src/Core/ArtasCore/ArtasCore.csproj
```

Если миграция с таким именем/таймстампом уже создана вручную — Designer и
снапшот правятся вручную по образцу соседних миграций (схема таблиц не меняется,
поэтому снапшот остаётся прежним).

---

## 2. Зарегистрируйте символ в конфиге Writer

Добавьте `Name` в секцию `Writer:Symbols` в **обоих** файлах:

- [`src/Writer/ArtasWriter.Worker/appsettings.json`](../src/Writer/ArtasWriter.Worker/appsettings.json)
- [`src/Writer/ArtasWriter.Worker/appsettings.example.json`](../src/Writer/ArtasWriter.Worker/appsettings.example.json)

```jsonc
"Symbols": [
  { "Name": "Si" },
  { "Name": "MX" }          // ← новый символ (name == symbols.name)
]
```

Разрешение имени: `symbols.name` (конфиг) → `symbols.symbol` (БД, код
контракта) → `{symbol}@{DefaultMic}` → подписка в Finam. MIC берётся из
`Finam:DefaultMic` (по умолчанию `RTSX` — фьючерсы MOEX FORTS).

> Reader (ArtasChart) правок **не требует**: список символов для выпадающего
> списка он подтягивает из `public.symbols` автоматически
> ([`LoadSymbolsAsync()`](../src/Reader/ArtasChart.App/ViewModels/MainWindowViewModel.cs)).

---

## 3. Примените миграцию

```powershell
.\db\apply-migrations.ps1            # применить к artas_db
.\db\apply-migrations.ps1 -List      # проверить список миграций
```

---

## 4. Проверьте результат

```sql
-- 1. Символ в таблице
SELECT id, name, symbol, description, digits, point FROM public.symbols ORDER BY id;

-- 2. Партиция создана
SELECT child.relname AS partition_name
FROM pg_inherits
JOIN pg_class parent ON pg_inherits.inhparent = parent.oid
JOIN pg_class child  ON pg_inherits.inhrelid  = child.oid
WHERE parent.relname = 'fractals'
ORDER BY child.relname;

-- 3. Partition pruning работает (должна быть одна партиция)
EXPLAIN SELECT * FROM fractals WHERE symbol_id = 6;
```

Проверьте health Writer — символ должен появиться в `/health` и подписаться в Finam:

```powershell
curl http://localhost:8080/health
# в ответе ожидается блок "symbol": "MX", "name": "MXZ6", "symbol_id": 6,
# а в логах — "Finam: подписка на котировки MXZ6@RTSX"
```

---

## 5. Обновите документацию

После успешного добавления символа обновите:

- [`db/database_schema.md`](../db/database_schema.md) — таблица партиций `fractals`
  и список миграций.
- [`db/README.md`](../db/README.md) — раздел «Начальные данные».

---

## Чек-лист

- [ ] Определены `id`, `name`, `symbol`, `description`, `digits`, `point`.
- [ ] Миграция: `INSERT ... ON CONFLICT (name) DO NOTHING` + `setval`.
- [ ] Миграция: партиция `fractals_<lower(name)>` + индекс `idx_fractals_<name>_time`.
- [ ] Down-миграция: `DROP TABLE` партиции + `DELETE FROM symbols`.
- [ ] Designer-файл и `ArtasDbContextModelSnapshot` согласованы.
- [ ] `Name` добавлен в `Writer:Symbols` (оба appsettings-файла).
- [ ] `.\db\apply-migrations.ps1` выполнен без ошибок.
- [ ] Проверены `symbols`, партиции, `EXPLAIN` (partition pruning), `/health`.
- [ ] Обновлены `db/database_schema.md` и `db/README.md`.
