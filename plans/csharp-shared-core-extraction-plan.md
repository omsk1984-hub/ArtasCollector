# План: вынос общих классов Reader и Writer в C#/src/Core

## Статус: ✅ ВЫПОЛНЕНО

Реализация завершена. Создан проект `ArtasCore`, общие классы перенесены,
Reader и Writer переключены на него. Оба решения собираются без ошибок
(0 предупреждений / 0 ошибок), 33/33 теста Writer проходят.

Итоговое пространство имён: **`ArtasCore`** (`ArtasCore.Models`, `ArtasCore.Json`,
`ArtasCore.Data`, `ArtasCore.Abstractions`).

## Цель

Устранить дублирование кода между проектами [`C#/src/Reader`](C#/src/Reader) и
[`C#/src/Writer`](C#/src/Writer) путём выноса общих классов (моделей, JSON-конвертеров,
абстракций доступа к БД, DTO) в новый общий проект [`C#/src/Core`](C#/src/Core).

## Анализ: выявленные дубликаты

### Полностью дублируемые модели (Models)

| Класс | Reader | Writer | Комментарий |
|---|---|---|---|
| `MarketPair` | `ArtasChart.Core.Models` | `ArtasWriter.Core.Models` | Практически идентичны; Writer имеет доп. конструктор `(bool isAsk, double price, int volume)` и другой `ToString()`. |
| `MyTick` | `ArtasChart.Core.Models` | `ArtasWriter.Core.Models` | Расхождения: Reader использует `FlexibleLongConverter`/`FlexibleIntConverter`, `Volume` типа `long`; Writer — без конвертеров, `Volume` типа `int`, + свойства `MAsk/MBid/Sum/Buy/Sell`. |
| `InfoF` | `ArtasChart.Core.Models` | `ArtasWriter.Core.Models` | Reader — с гибкими конвертерами и именованными свойствами стакана; Writer — без конвертеров + статический `Create(InfoMeta)`. |
| `StatF` | `ArtasChart.Core.Models` | `ArtasWriter.Core.Models` | Reader — с гибкими конвертерами; Writer — без них. |
| `SymbolInfo` | `ArtasChart.Core.Models` | `ArtasWriter.Core.Models` | Идентичны по логике, различаются только XML-комментарии. |
| `Fractal` | `ArtasChart.Core.Models` | `ArtasWriter.Core.Models` | Reader: только свойства. Writer: + конструктор `(bool isUp, MyTick pickTick)`, `InfoJson`/`PeakJson`, конструктор по умолчанию, другой `ToString()`. |

### Дублируемые вспомогательные классы

| Класс | Reader | Writer | Комментарий |
|---|---|---|---|
| `FlexibleLongConverter` / `FlexibleIntConverter` | `ArtasChart.Core.Json` | — | Есть только в Reader. Нужны Writer-моделям после унификации. |
| `DbConfig` | `ArtasChart.Data` | `ArtasWriter.Infrastructure.Data` | Различаются: Reader читает `DB_*`, Writer — `Db:*` и `DB_*`. |
| `IHealthProbe` | `ArtasChart.Data` | `ArtasWriter.Core.Abstractions` | Идентичны по сигнатуре. |
| `FractalMetadataDto` | `ArtasChart.Data.JsonModels` | `ArtasWriter.Infrastructure.Data` | Reader — с гибкими конвертерами; Writer — + `FromFractal(Fractal)`. |

### Классы, которые остаются в своих проектах (не общие)

- Writer-only: `FractalFactory`, `InfoMeta`, `TempPair`, `MarketPairBufferItem`, `TickFlags`.
  > Примечание: `TempPair`, `InfoMeta`, `MarketPairBufferItem`, `TickFlags` — часть
  > алгоритмического ядра Writer и используются только им. Оставляем в Writer.
- Reader-only: `IFractalReader`, `IFractalCounter`, сервисы `ArtasChart.Services`.
- Различающиеся интерфейсы репозиториев: `IFractalReader` (Reader, чтение) и
  `IFractalRepository` (Writer, запись) — семантически разные, объединять не следует.

### Решение по конфликтам моделей

Объединённая версия каждой модели берёт **надмножество** обеих реализаций:

- Числовые поля — тип `long` с гибкими конвертерами (совместимо с JSONB Python,
  где int/float не различаются); `int`-поля Writer при этом расширяются до `long`,
  что безопасно для вычислений.
- Из Writer-версий добавляются конструкторы (`Fractal(bool, MyTick)`,
  `MarketPair(bool, double, int)`), свойства (`MAsk`, `MBid`, `Sum`, `Buy`, `Sell`),
  `TickFlags` и статические фабрики (`InfoF.Create`, `FractalMetadataDto.FromFractal`).
- `ToString()` берётся из наиболее информативного варианта (Writer).
- `TickFlags` переносится в Core, поскольку `MyTick.IsBuy` (Writer-версия) от него зависит.

## Целевая структура

```
C#/src/Core/
  ArtasCore/
    ArtasCore.csproj
    Json/
      FlexibleNumberConverters.cs   (FlexibleLongConverter, FlexibleIntConverter)
    Models/
      MarketPair.cs
      MyTick.cs
      InfoF.cs
      StatF.cs
      SymbolInfo.cs
      Fractal.cs
      TickFlags.cs                  (нужен MyTick.IsBuy)
    Data/
      DbConfig.cs
      FractalMetadataDto.cs
    Abstractions/
      IHealthProbe.cs
      IFractalReader.cs
      IFractalRepository.cs
    Directory.Build.props           (скопировать настройки Reader/Writer)
```

Единое пространство имён: **`ArtasCore`** (подпапки дают `ArtasCore.Json`,
`ArtasCore.Models`, `ArtasCore.Data`, `ArtasCore.Abstractions`).

## Шаги выполнения

### Шаг 1. Создать проект `C#/src/Core/ArtasCore`

1. Создать [`C#/src/Core/ArtasCore/ArtasCore.csproj`](C#/src/Core/ArtasCore/ArtasCore.csproj)
   (SDK `Microsoft.NET.Sdk`, `net10.0`), добавить пакеты:
   `Npgsql`, `Microsoft.Extensions.Configuration*` (для `DbConfig`).
2. Создать [`C#/src/Core/Directory.Build.props`](C#/src/Core/Directory.Build.props)
   с `TargetFramework=net10.0`, `Nullable`, `ImplicitUsings`, `NuGetAudit=false`.

### Шаг 2. Перенести общие модели

Перенести в [`ArtasCore/Models`](C#/src/Core/ArtasCore/Models) объединённые версии:

1. `MarketPair.cs` — Reader-версия + конструктор и `ToString()` из Writer.
2. `MyTick.cs` — Writer-версия (`MAsk/MBid/Sum/Buy/Sell`) + гибкие конвертеры,
   `Volume` — `long`.
3. `InfoF.cs` — Reader-версия (гибкие конвертеры, именованные свойства стакана)
   + статический `Create(InfoMeta)` — **проблема**: `InfoMeta` Writer-only.
   → Вынести `InfoMeta`, `TempPair`, `MarketPairBufferItem` в Core тоже
   (они нужны `InfoF.Create` и `FractalFactory`). См. уточнение ниже.
4. `StatF.cs` — Reader-версия с гибкими конвертерами.
5. `SymbolInfo.cs` — объединённая (идентичны).
6. `Fractal.cs` — Writer-версия (конструкторы, `InfoJson`/`PeakJson`) + `using` конвертеров.
7. `TickFlags.cs` — из Writer.

> **Уточнение по `InfoMeta`:** `InfoF.Create(InfoMeta)` и `FractalFactory` завязаны
> на `InfoMeta`/`TempPair`. Поскольку `FractalFactory` относится к ядру Writer,
> есть два варианта:
> - **(A)** Вынести `InfoMeta`, `TempPair` в Core вместе с моделями (тогда Core
>   включает почти всё ядро Writer), а `FractalFactory`/`MarketPairBufferItem`
>   оставить в Writer.
> - **(B)** Оставить `InfoMeta`/`TempPair` в Writer, а `InfoF.Create` перенести
>   в Writer-расширение (`InfoFFactory`).
>
> **Принято (A)** — минимально инвазивно: `InfoMeta` и `TempPair` становятся
> общими, `InfoF.Create` остаётся в Core.

### Шаг 3. Перенести Json-конвертеры

Перенести [`FlexibleNumberConverters.cs`](C#/src/Reader/ArtasChart.Core/Json/FlexibleNumberConverters.cs)
в `ArtasCore/Json`.

### Шаг 4. Перенести данные и абстракции

1. `DbConfig.cs` — объединённая версия: поддержка и `Db:*`, и `DB_*` (версия Writer).
2. `FractalMetadataDto.cs` — Reader-версия (гибкие конвертеры) + `FromFractal` (Writer).
3. `IHealthProbe.cs` — единый.
4. `IFractalReader.cs` (Reader) и `IFractalRepository.cs` (Writer) — перенести обе,
   т.к. зависят от моделей Core.

### Шаг 5. Обновить Reader

1. [`ArtasChart.Core.csproj`](C#/src/Reader/ArtasChart.Core/ArtasChart.Core.csproj):
   превратить в обёртку — удалить файлы моделей/Json, добавить
   `ProjectReference` на `ArtasCore.csproj`.
2. [`ArtasChart.Data.csproj`](C#/src/Reader/ArtasChart.Data/ArtasChart.Data.csproj):
   удалить `DbConfig.cs`, `IHealthProbe.cs`, `IFractalReader.cs`,
   `JsonModels/FractalMetadataDto.cs`; добавить `ProjectReference` на `ArtasCore`.
3. Заменить `using ArtasChart.Core.Models;` → `using ArtasCore.Models;`,
   `using ArtasChart.Core.Json;` → `using ArtasCore.Json;`,
   `using ArtasChart.Data.JsonModels;` → `using ArtasCore.Data;` и т.п.
4. Обновить [`ArtasChart.sln`](C#/src/Reader/ArtasChart.sln): добавить проект `ArtasCore`.

### Шаг 6. Обновить Writer

1. [`ArtasWriter.Core.csproj`](C#/src/Writer/ArtasWriter.Core/ArtasWriter.Core.csproj):
   удалить перенесённые модели, добавить `ProjectReference` на `ArtasCore`.
2. [`ArtasWriter.Infrastructure.csproj`](C#/src/Writer/ArtasWriter.Infrastructure/ArtasWriter.Infrastructure.csproj):
   удалить `DbConfig.cs`, `FractalMetadataDto.cs`; `ProjectReference` на `ArtasCore`.
3. Заменить `using ArtasWriter.Core.Models;` → `using ArtasCore.Models;`,
   `using ArtasWriter.Core.Abstractions;` → `using ArtasCore.Abstractions;`,
   `using ArtasWriter.Infrastructure.Data;` → `using ArtasCore.Data;`.
4. Обновить [`ArtasWriter.slnx`](C#/src/Writer/ArtasWriter.slnx): добавить `ArtasCore`.

### Шаг 7. Сборка и проверка

1. `dotnet build C#/src/Reader/ArtasChart.sln`.
2. `dotnet build C#/src/Writer/ArtasWriter.slnx`.
3. `dotnet test C#/src/Writer/ArtasWriter.Tests` — убедиться, что тесты проходят.

## Риски и меры

- **Конфликт `Volume: int` vs `long`.** После унификации до `long` — проверить
  компиляцию Writer (`int` → `long` неявно расширяется, обратные приведения могут
  потребовать `(int)`).
- **Пространства имён.** Массовая замена `using` — критичный шаг; выполнять
  поиском по `using ArtasChart.` / `using ArtasWriter.` и сверять число совпадений.
- **`InfoMeta` зависит от `MyTick`**, а `FractalFactory` — от `InfoMeta`; порядок
  переноса: модели → `InfoMeta`/`TempPair` → фабрики.
- **`Directory.Build.props`.** Оба корня (`Reader`, `Writer`) имеют свой props с
  `TargetFramework`. Между ними и `Core` нужно определиться: либо Core имеет
  собственный props, либо наследует от общего `C#/src/Directory.Build.props`.

## Порядок коммитов (рекомендуемый)

1. Создание `ArtasCore` + перенос моделей/Json (без правок потребителей).
2. Переключение Reader на `ArtasCore`.
3. Переключение Writer на `ArtasCore`.
4. Удаление временных обёрток, финальная сборка.
