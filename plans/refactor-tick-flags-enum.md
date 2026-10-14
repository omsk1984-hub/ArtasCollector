# План: рефакторинг флагов тика в `[Flags] enum TickFlag` (Вариант A)

## 1. Контекст и проблема

Сейчас флаги тика — это «сырой» `int` с набором констант в
[`TickFlags`](C#/src/Core/ArtasCore/Models/TickFlags.cs:6):

```csharp
public static class TickFlags
{
    public const int Bid = 2;
    public const int Ask = 4;
    public const int Last = 8;
    public const int Volume = 16;
    public const int Buy = 32;
    public const int Sell = 64;

    public static bool? IsBuy(int flags) { ... }
}
```

Проблемы текущего подхода:

1. **Нет типизации.** Любой `int` совместим с `Flags`, компилятор не ловит ошибки
   (например, передачу `Volume` туда, где ожидается битовый набор).
2. **Дублирование логики сборки.** Ручное `|=` разбросано по транспорту
   ([`FinamGrpcClient.PublishTrade`](C#/src/Writer/ArtasWriter.FinamApi/FinamGrpcClient.cs:394),
   [`PublishQuote`](C#/src/Writer/ArtasWriter.FinamApi/FinamGrpcClient.cs:432),
   [`SimulationEventSource`](C#/src/Writer/ArtasWriter.FinamApi/Simulation/SimulationEventSource.cs:41)).
3. **Утечка модели ядра в транспорт.** [`FinamGrpcClient`](C#/src/Writer/ArtasWriter.FinamApi/FinamGrpcClient.cs:394)
   напрямую обращается к `ArtasCore.Models.TickFlags.*` — нарушение DIP.
4. **«Мёртвые» константы.** Ядро ([`FractalFactory`](C#/src/Writer/ArtasWriter.Core/Models/FractalFactory.cs),
   [`InfoMeta`](C#/src/Core/ArtasCore/Models/InfoMeta.cs)) читает только `IsBuy`
   (→ `Buy`/`Sell`). Константы `Bid`, `Ask`, `Last`, `Volume` выставляются, но не читаются.

## 2. Цель

Заменить набор `const int` на строго типизированный `[Flags] enum TickFlag`:

```csharp
[Flags]
public enum TickFlag
{
    None   = 0,
    Bid    = 2,
    Ask    = 4,
    Last   = 8,
    Volume = 16,
    Buy    = 32,
    Sell   = 64,
}
```

При этом **сохранить битовые значения** (2/4/8/16/32/64), чтобы JSON/БД-контракт и
Python-совместимость не изменились.

## 3. Критические точки совместимости (сначала изучить, потом менять)

| Точка | Файл | Что важно |
|---|---|---|
| Сериализация `MyTick.Flags` | [`MyTick.cs:41`](C#/src/Core/ArtasCore/Models/MyTick.cs:41) | `[JsonConverter(typeof(FlexibleIntConverter))]` — конвертер под `int`, при смене типа потребуется правка/новый конвертер |
| Контракт события | [`IFinamClient.cs:19`](C#/src/Writer/ArtasWriter.Core/Abstractions/IFinamClient.cs:19) | `QuoteEvent.Flags` — `int`; менять тип → затронет транспорт и тесты |
| Метаданные фрактала | [`FractalMetadataDto.cs`](C#/src/Core/ArtasCore/Data/FractalMetadataDto.cs) | `peak`/`MyTick` уходит в JSON; проверить, что `Flags` не ломает формат БД |
| Разбор направления | [`TickFlags.IsBuy`](C#/src/Core/ArtasCore/Models/TickFlags.cs:19) | Единственный потребитель битов направления |
| Конвертеры | [`FlexibleNumberConverters.cs:49`](C#/src/Core/ArtasCore/Json/FlexibleNumberConverters.cs:49) | `FlexibleIntConverter : JsonConverter<int>` |

**Вывод по совместимости:** полный переход на `enum TickFlag` ломает:
- `JsonConverter<int>` на `MyTick.Flags`;
- тип `QuoteEvent.Flags` (публичный контракт `IFinamClient`);
- совместимость с Python (в Python `flags` — целое число из MT5).

Поэтому рекомендуется **гибридный подход**: ввести `enum TickFlag`, но сохранить
`int`-совместимость через конвертер и явные приведения.

## 4. Два подварианта реализации

### A1. Полный переход (enum в публичном контракте)

Меняем тип `Flags` на `TickFlag` во всех слоях: `MyTick`, `QuoteEvent`, `IFinamClient`,
транспорт, тесты.

- **Плюс:** максимальная типобезопасность.
- **Минус:** ломает JSON/БД-контракт (нужен `JsonConverter<TickFlag>`), меняет публичный
  `IFinamClient` (затронет Reader и другие потребители), расходится с Python.
- **Риск:** высокий.

### A2. Гибрид (рекомендуется) ⭐

Вводим `enum TickFlag` как **основной тип для сборки/чтения битов** в C#-коде, но:

- `MyTick.Flags` остаётся `int` в контракте (JSON/БД), но добавляется
  `[JsonIgnore] public TickFlag FlagsEnum => (TickFlag)Flags;` и/или сеттер;
- вводится `JsonConverter<TickFlag>` (наследник логики `FlexibleIntConverter`) —
  на случай, если где-то enum уйдёт в JSON;
- хелпер [`TickFlagExtensions`] с методами `Has`, `With`, `ForDirection`.

- **Плюс:** типобезопасность внутри, нулевой риск для JSON/БД/Python.
- **Минус:** чуть больше кода (дуальность `int`/`enum`).
- **Риск:** низкий.

## 5. Пошаговый план (A2 — к внедрению)

### Шаг 1. Ввести `enum TickFlag` и расширения
- Создать [`TickFlag`](C#/src/Core/ArtasCore/Models/TickFlag.cs) (`[Flags]`, значения 2/4/8/16/32/64).
- Создать `TickFlagExtensions`:
  - `bool Has(this TickFlag flags, TickFlag mask)`;
  - `TickFlag With(this TickFlag flags, TickFlag add)`;
  - `bool? Direction(this TickFlag flags)` (аналог `IsBuy`);
  - `TickFlag ForTrade(TradeSide side, bool changedLast, bool changedVolume)`.

### Шаг 2. Ввести `enum TradeSide` для `Trade.side`
- `enum TradeSide { Buy, Sell }` в `ArtasWriter.Application` или `ArtasCore`.
- Маппинг `Grpc.Tradeapi.V1.Side` → `TradeSide` в транспорте (private static метод),
  чтобы [`FinamGrpcClient`](C#/src/Writer/ArtasWriter.FinamApi/FinamGrpcClient.cs) не
  работал с protobuf-`Side` напрямую.

### Шаг 3. Перевести сборку флагов на enum
- [`PublishTrade`](C#/src/Writer/ArtasWriter.FinamApi/FinamGrpcClient.cs:394):
  ```csharp
  var flags = TickFlag.Last | TickFlag.Volume | TickFlag.ForDirection(side);
  ```
- [`PublishQuote`](C#/src/Writer/ArtasWriter.FinamApi/FinamGrpcClient.cs:432):
  ```csharp
  var flags = last is not null ? TickFlag.Last : TickFlag.None;
  ```
- [`SimulationEventSource`](C#/src/Writer/ArtasWriter.FinamApi/Simulation/SimulationEventSource.cs:41):
  ```csharp
  var flags = rnd.Next(2) == 0 ? TickFlag.Buy : TickFlag.Sell;
  ```
- В `QuoteEvent` на границе приводить к `int` (`(int)flags`), чтобы не менять контракт.

### Шаг 4. Совместимость `MyTick`
- Оставить `public int Flags { get; init; }`.
- Добавить `[JsonIgnore] public TickFlag FlagsEnum => (TickFlag)Flags;`.
- `IsBuy` реализовать через `FlagsEnum.Direction()`.
- При необходимости — `JsonConverter<TickFlag>` (для будущего перехода).

### Шаг 5. Убрать старый `TickFlags`
- После миграции всех вызовов удалить `static class TickFlags` (или оставить как фасад
  с `[Obsolete]` на переходный период).

### Шаг 6. Тесты
- Unit для `TickFlag`/`TickFlagExtensions`: `Has`, `With`, `Direction`,
  приоритет `Buy` над `Sell` (как в [`ModelTests.cs:16`](C#/src/Writer/ArtasWriter.Tests/Core/ModelTests.cs:16)).
- Unit для маппинга `Grpc.Tradeapi.V1.Side` → `TradeSide`.
- Обновить [`ModelTests.cs`](C#/src/Writer/ArtasWriter.Tests/Core/ModelTests.cs),
  [`FractalFactoryTests.cs`](C#/src/Writer/ArtasWriter.Tests/Core/FractalFactoryTests.cs),
  [`TickServiceTests.cs`](C#/src/Writer/ArtasWriter.Tests/Application/TickServiceTests.cs),
  [`FractalPipelineTests.cs`](C#/src/Writer/ArtasWriter.Tests/Application/FractalPipelineTests.cs).
- Regression: сгенерировать фрактал и проверить, что `Flags` в JSON не изменились.

### Шаг 7. Верификация
- `dotnet test` для [`ArtasWriter.Tests`](C#/src/Writer/ArtasWriter.Tests).
- Проверить JSON-совместимость: сериализовать `MyTick` до и после, diff должен быть пустым.
- Проверить, что `/health` не затронут (флаги не участвуют в health-ответе).

## 6. Что НЕ решает вариант A

**Важно:** замена флагов на enum **не устраняет** нулевые `ask`/`bid` в `/health`.
Корневая причина — [Trade](C#/src/Writer/ArtasWriter.FinamApi/Protos/grpc/tradeapi/v1/marketdata/marketdata_service.proto:440)
не содержит Bid/Ask, а [`PublishTrade`](C#/src/Writer/ArtasWriter.FinamApi/FinamGrpcClient.cs:400)
подставляет `0d`. Это отдельная задача (кэш последней котировки либо `Last → Ask/Bid по side`).

## 7. Порядок относительно других задач

1. **Приоритет 1:** источник Bid/Ask (устраняет баг `/health`).
2. **Приоритет 2:** `enum TradeSide` (чистота транспорта).
3. **Приоритет 3:** `enum TickFlag` (этот план).
4. **Не рекомендуется:** полный переход `Flags` на enum в публичном контракте (лом JSON/БД).

Вариант A (гибрид A2) безопасно делать **после** закрытия бага с Bid/Ask, чтобы не смешивать
два изменения в одном регрессионном прогоне.
