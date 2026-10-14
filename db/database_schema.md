# База данных Artas2026 (artas_db)

## Общая информация

База данных **artas_db** используется в проекте Artas2026 для хранения финансовых данных, полученных из MetaTrader 5, а также результатов технического анализа (фракталы). Она построена на PostgreSQL и развертывается через Docker.

**Основные сущности:**
- **Символы (валютные пары)** – торгуемые инструменты
- **Фракталы** – обнаруженные паттерны технического анализа (верхние/нижние)
- **Настройки фильтра** – пользовательские параметры фильтра фракталов по символам

## Схема таблиц

### Таблица `symbols`

Хранит информацию о финансовых инструментах.

| Поле | Тип | Описание |
|------|-----|----------|
| `id` | SERIAL | Первичный ключ, автоинкремент |
| `symbol` | VARCHAR(20) | Уникальный код символа (например, "EURUSD") |
| `description` | TEXT | Описание символа |
| `digits` | INTEGER | Количество знаков после запятой |
| `point` | DOUBLE PRECISION | Минимальное изменение цены (шаг) |
| `created_at` | TIMESTAMP WITH TIME ZONE | Дата создания записи |

**Индексы:**
- Уникальный индекс на `symbol`

**Комментарий:** "Валютные пары (символы) торгуемые в MetaTrader"


### Таблица `fractals` (Partitioned Table)

Хранит обнаруженные фракталы (паттерны технического анализа). **Рартиционирована** по `symbol_id` (LIST partitioning) — каждый символ хранится в отдельной партиции.

| Поле | Тип | Описание |
|------|-----|----------|
| `id` | BIGINT | Часть составного первичного ключа вместе с `symbol_id`, ручное заполнение (обычно `time_msc`) |
| `symbol_id` | INTEGER | Внешний ключ на `symbols.id`, часть составного первичного ключа, **ключ партиционирования** |
| `price` | DOUBLE PRECISION | Цена фрактала |
| `time` | TIMESTAMP WITH TIME ZONE | Время формирования фрактала |
| `metadata` | JSONB | Дополнительные метаданные (сила, количество баров и т.д.) |
| `stat` | JSONB | Статистические данные фрактала (произвольный JSON) |
| `deleted` | BOOLEAN | Флаг удаления (soft delete), по умолчанию `false` |
| `created_at` | TIMESTAMP WITH TIME ZONE | Дата создания записи |

**Партиционирование:** `PARTITION BY LIST (symbol_id)`

| Партиция | Тип | Описание |
|----------|-----|----------|
| `fractals_si` | `FOR VALUES IN (1)` | Фракталы Si (контракт SiZ6) |
| `fractals_cny` | `FOR VALUES IN (2)` | Фракталы CNY (контракт CRZ6) |
| `fractals_usdrubf` | `FOR VALUES IN (3)` | Фракталы USDRUBF |
| `fractals_cnyrubf` | `FOR VALUES IN (4)` | Фракталы CNYRUBF |
| `fractals_imoexf` | `FOR VALUES IN (5)` | Фракталы IMOEXF (MOEX) |
| `fractals_mx` | `FOR VALUES IN (6)` | Фракталы MX (MOEX max, контракт MXZ6) |
| `fractals_mm` | `FOR VALUES IN (7)` | Фракталы MM (MOEX mini, контракт MMZ6) |
| `fractals_ri` | `FOR VALUES IN (8)` | Фракталы RI (RTS, контракт RIZ6) |
| `fractals_default` | `DEFAULT` | Ловушка для неизвестных символов |

> Имя партиции строится по `name` символа (постоянный ключ поиска), а не по коду
> контракта `symbol`. Например, символ `name='CNY'` (`symbol='CRZ6'`) хранится в
> партиции `fractals_cny`.

**Индексы:**
- `fractals_pkey1` – составной первичный ключ (`id`, `symbol_id`)
- `idx_fractals_symbol_time` на (`symbol_id`, `time DESC`) для быстрого поиска по символу и времени
- Каждая партиция имеет собственный индекс `idx_fractals_{symbol}_time`

**Триггер:** `trg_create_fractal_partition` на таблице `symbols` — автоматически создаёт новую партицию при добавлении нового символа.

**Комментарий:** "Обнаруженные фракталы (технический анализ) с составным первичным ключом (id, symbol_id), партиционированная по symbol_id"

### Таблица `symbol_filter_settings`

Хранит пользовательские настройки фильтра фракталов **по каждому символу** (одна запись на символ).
Используется графиком Reader (ArtasChart): при нажатии «Применить фильтр» настройки сохраняются
(upsert), при нажатии «Загрузить» — восстанавливаются из БД. Все параметры фильтра хранятся
в одном JSONB-поле `settings`.

| Поле | Тип | Описание |
|------|-----|----------|
| `id` | SERIAL | Первичный ключ, автоинкремент |
| `symbol_id` | INTEGER | Внешний ключ на `symbols.id`, **уникален** (одна запись на символ), `ON DELETE CASCADE` |
| `settings` | JSONB | Параметры фильтра (по умолчанию `{}`) |
| `created_at` | TIMESTAMP WITH TIME ZONE | Дата создания записи |
| `updated_at` | TIMESTAMP WITH TIME ZONE | Дата последнего сохранения настроек |

**Структура `settings` (JSONB):**

| Ключ | Тип | Описание |
|------|-----|----------|
| `MinMarket0Volume` | number | Порог по `Markets[0].Volume` (Ask). 0 — выключен |
| `MinMarket1Volume` | number | Порог по `Markets[1].Volume` (Bid). 0 — выключен |
| `MinVolume` | number | Порог по `Info.Volume`. 0 — выключен |
| `ExtremumNeighbors` | number | N фильтра локального экстремума (0 — выключен) |
| `ShowRedPoints` | boolean | Показывать ли точки-невзятия (красные/зелёные) |

**Индексы:**
- `uq_symbol_filter_settings_symbol_id` – уникальный индекс по `symbol_id` (обеспечивает upsert через `ON CONFLICT (symbol_id) DO UPDATE`)

**Комментарий:** "Пользовательские настройки фильтра фракталов по символам"

## Взаимосвязи (ER‑диаграмма)

```
symbols (1) ──< fractals (many)
symbols (1) ──  symbol_filter_settings (0..1)
```

- Один символ может иметь множество фракталов.
- Один символ имеет не более одной записи настроек фильтра (`symbol_filter_settings`).
- Фракталы ссылаются на символ через `symbol_id` с каскадным удалением (`ON DELETE CASCADE`).
- Настройки фильтра ссылаются на символ через `symbol_id` с каскадным удалением (`ON DELETE CASCADE`).

## Индексы

Для оптимизации запросов созданы следующие индексы:

1. `idx_fractals_symbol_time` – поиск фракталов по символу и времени (обратный порядок). Каждая партиция также имеет собственный индекс `idx_fractals_{symbol}_time`.

## Примеры запросов

### Получить фракталы для CNY / CRZ6 (автоматический partition pruning)

```sql
-- PostgreSQL автоматически выберет только партицию fractals_cny
SELECT * FROM fractals
WHERE symbol_id = 2
ORDER BY time DESC
LIMIT 100;
```

### Просмотр информации о партициях

```sql
SELECT
    parent.relname AS parent_table,
    child.relname AS partition_name,
    pg_size_pretty(pg_relation_size(child.oid)) AS partition_size
FROM pg_inherits
JOIN pg_class parent ON pg_inherits.inhparent = parent.oid
JOIN pg_class child ON pg_inherits.inhrelid = child.oid
WHERE parent.relname = 'fractals'
ORDER BY child.relname;
```

### Проверка partition pruning (EXPLAIN)

```sql
-- Должно показать только одну партицию
EXPLAIN SELECT * FROM fractals WHERE symbol_id = 4;
```

## Миграции

Схема базы данных управляется **EF Core миграциями** (проект [`ArtasCore`](../src/Core/ArtasCore)).

### Расположение

- Контекст: `src/Core/ArtasCore/Data/ArtasDbContext.cs`
- Сущности: `src/Core/ArtasCore/Data/Entities/`
- Конфигурации (Fluent API): `src/Core/ArtasCore/Data/Configurations/`
- Миграции: `src/Core/ArtasCore/Data/Migrations/`
- Design-time фабрика: `src/Core/ArtasCore/Data/ArtasDbContextFactory.cs`

### Применение миграций

```powershell
# Применить миграции к БД (по умолчанию artas_db на 127.0.0.1:5432)
.\db\apply-migrations.ps1

# Показать список миграций
.\db\apply-migrations.ps1 -List

# Baseline для УЖЕ существующей БД (пометить миграции применёнными без выполнения DDL)
.\db\apply-migrations.ps1 -Baseline
```

Строка подключения по умолчанию берётся из `DbConfig` (127.0.0.1:5432/artas_db) и может
быть переопределена переменной окружения `ARTAS_MIGRATIONS_CONNECTION`.

### Миграции

Схема создаётся **единственной объединённой миграцией** `InitialCreate`
(прежние миграции `SeedInitialData`, `AddSymbolFilterSettings`, `AddCnyAndImoexf`,
`AddCnyrubfParamsAndRenameSiPartition`, `AddMxMmRi` схлопнуты в неё).

- `InitialCreate` – создание финальной схемы «с нуля»:
  - таблицы `symbols`, `fractals` (partitioned by LIST `symbol_id`),
    `symbol_filter_settings` (уникальный `symbol_id`, JSONB-поле `settings`,
    `created_at`/`updated_at`), индексов, уникальных ограничений,
    DEFAULT‑партиции `fractals_default`.
    DDL партиционированной таблицы задаётся raw SQL (EF Core не поддерживает декларативное
    партиционирование PostgreSQL); нативный EF‑код используется для остальных объектов.
  - начальные данные: символы с фиксированными `id`:
    `Si` (`id=1`, `SiZ6`, `digits=2`, `point=0.01`),
    `CNY` (`id=2`, `CRZ6`, `digits=3`, `point=0.001`),
    `USDRUBF` (`id=3`, `USDRUBF`, `digits=2`, `point=0.01`),
    `CNYRUBF` (`id=4`, `CNYRUBF`, `digits=3`, `point=0.001`),
    `IMOEXF` (`id=5`, `IMOEXF`, `digits=1`, `point=0.5`),
    `MX` (`id=6`, `MXZ6`, `digits=0`, `point=25`),
    `MM` (`id=7`, `MMZ6`, `digits=2`, `point=0.05`),
    `RI` (`id=8`, `RIZ6`, `digits=0`, `point=10`).
    После вставки последовательность `symbols_id_seq` подтягивается до `MAX(id)`.
  - партиции `fractals_<name>` под каждый символ (имя партиции — по `name`):
    `fractals_si`, `fractals_cny`, `fractals_usdrubf`, `fractals_cnyrubf`,
    `fractals_imoexf`, `fractals_mx`, `fractals_mm`, `fractals_ri`.

### Партиционирование `fractals`

Таблица `fractals` объявлена как `PARTITION BY LIST (symbol_id)`. При добавлении
нового символа требуется создать соответствующую партицию вручную (SQL):

```sql
-- Имя партиции — по name символа (например, для name='CNY' → fractals_cny)
CREATE TABLE fractals_<name> PARTITION OF fractals FOR VALUES IN (<symbol_id>);
CREATE INDEX idx_fractals_<name>_time ON fractals_<name>(time DESC);
```

Новые символы попадают в DEFAULT‑партицию `fractals_default`, пока для них не создана
персональная партиция.

### Прежние SQL‑скрипты (архив)

Исторические SQL‑скрипты перемещены в `db/init-scripts/archive/` и сохранены
как архив для справки; они больше не применяются автоматически. Их логика
перенесена в единую миграцию `InitialCreate`.

## Развертывание

База данных разворачивается через Docker Compose. Подробности в [README.md](README.md).

### Параметры подключения

- **Хост:** `localhost`
- **Порт:** `5432`
- **База данных:** `artas_db`
- **Пользователь:** `artas_user`
- **Пароль:** `artas_password` (по умолчанию)

### Переменные окружения

Можно переопределить в файле `.env`:

```bash
POSTGRES_DB=artas_db
POSTGRES_USER=artas_user
POSTGRES_PASSWORD=artas_password
POSTGRES_PORT=5432
```

## Примечания для разработчиков

1. **Идентификаторы фракталов** – поле `id` в таблице `fractals` не автоинкрементное, обычно заполняется значением `time_msc` (время в мс) для обеспечения уникальности.
2. **JSONB метаданные** – в поле `metadata` можно хранить произвольные структуры (например, `{"strength": 0.8, "bars": 5}`). Используйте операторы PostgreSQL для работы с JSONB.
3. **Часовые пояса** – все поля `TIMESTAMP WITH TIME ZONE` хранятся в UTC. При выборке учитывайте преобразование в локальное время.

## Дальнейшее развитие

Возможные расширения схемы:

- Добавление таблицы `candles` для агрегированных свечей (M1, H1 и т.д.).
- Таблица `strategies` для хранения параметров торговых стратегий.
- Таблица `signals` для торговых сигналов, сгенерированных на основе фракталов.

---

*Документ обновлён: 2026‑04‑21*