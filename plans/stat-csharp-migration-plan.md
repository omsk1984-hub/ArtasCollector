# План миграции Stat-сервиса на C# (.NET 10)

## Цель

Перенести Python-сервис статистики
`scripts/manage_stat_writer.ps1` → `C#/scripts/run_stat_writer.py` →
`artas2026.services.stat_service.StatService` + `artas2026.core.stat_cursor.StatCursor`
на .NET 10 в `C#/src/Stat` по подобию `C#/src/Writer`.

## Согласованные решения

1. **Batch-job**, а не вечный стриминг: читает фракталы из БД чанками, считает stat
   курсором, пишет обратно, завершается. Exit code 0 — успех, 1 — ошибка.
2. **Переиспользование ArtasCore**: модели `Fractal`, `MyTick`, `InfoF`, `StatF`,
   `SymbolInfo`, `DbConfig`, `FractalMetadataDto`; абстракции `IFractalReader`,
   `IStatRepository`. **Stat.Core не создаётся.**
3. Структура: `Stat.Application`, `Stat.Infrastructure`, `Stat.Worker`, `Stat.Tests`
   + `Stat.slnx` + `run_Stat.ps1`.
4. Health-эндпоинт не требуется; прогресс — в логах.

## Структура файлов

```
C#/src/Stat/
├── Stat.slnx
├── Directory.Build.props
├── NuGet.config
├── .gitignore
├── README.md
├── run_Stat.ps1
├── Stat.Application/
│   ├── Stat.Application.csproj
│   ├── StatCursor.cs
│   ├── Options/StatOptions.cs
│   ├── Abstractions/IStatWalker.cs
│   ├── Buffers/CircularBuffer.cs
│   └── Services/FractalBufferService.cs
├── Stat.Infrastructure/
│   ├── Stat.Infrastructure.csproj
│   ├── StatModule.cs
│   ├── Data/StatRepository.cs
│   ├── Data/FractalReadRepository.cs
│   └── Services/
│       ├── FractalServiceFromDb.cs
│       └── StatWalker.cs
├── Stat.Worker/
│   ├── Stat.Worker.csproj
│   ├── Program.cs
│   ├── DependencyInjection.cs
│   ├── StatRunner.cs
│   ├── appsettings.json
│   └── appsettings.example.json
└── Stat.Tests/
    ├── Stat.Tests.csproj
    └── Application/
        ├── StatCursorTests.cs
        └── FractalBufferServiceTests.cs
```

Изменения в `C#/src/Core/ArtasCore`:

- `Models/StatF.cs` — добавлены поле `HasBuy` (`[JsonPropertyName("has_buy")]`, default true)
  и свойства `IsOpen`/`IsStop` (по аналогии с существующим `IsTake`).
- `Abstractions/IStatRepository.cs` (новый) — `ClearStatAsync`, `UpdateStatAsync`,
  `UpdateStatBatchAsync`.

## Перенос логики: StatCursor

- `Init(pick, hasBuy)`: `dealSign`; `price_open = pick + 1*point*sign`;
  `price_stop = open - stop*point*sign`; `price_take = open + take*point*sign`;
  округление `Math.Round(x, digits, MidpointRounding.ToEven)`.
- `Check(stat, fractal, index)`: возвращает `(StatF, bool IsStopped)` (из-за иммутабельности
  `StatF`-record вместо мутации по месту). Знаковая логика buy/sell идентична Python.
  `result_stop = stop_point + 1`; `result_take = take_point` либо `(int)((Price-open)/point)`.

## Перенос логики: буфер и StatWalker

- `CircularBuffer<T>` воспроизводит Python `ListClass`:
  - `Count` — общее число добавленных (растёт бесконечно);
  - `GetByIndex(index)` = `self[Count - index - 1]`, при `newIndex >= capacity` → default (None);
  - `GetByOffset(i)` = `self[i]`.
- `FractalBufferService` (Stat-версия) эмитит callback `(Fractal, CircularBuffer<Fractal>)`,
  что даёт `_on_fractal` доступ к `Count` и `get_by_index`.
- `StatWalker.OnFractal` — прямой перенос `StatService._on_fractal`:
  `endIndex = CurrentFIndex + MaxDeep`; при `Count < endIndex` — выход;
  `current = GetByIndex(CurrentFIndex)`; `Init`; цикл `i` от `CurrentFIndex+1`
  до `endIndex`; `SaveStatisticsToDb`; `CurrentFIndex++`.
- `RunAsync` — перенос `StatService.start`: опциональный `clear`, цикл
  `FractalsFromLastAsync(limit=chunk, hasStat:false)` пока count > 0, лог `Saved/total`.

## БД

- `FractalReadRepository : IFractalReader` — чтение фракталов и symbol info.
- `StatRepository : IStatRepository`:
  - `ClearStatAsync`: `UPDATE fractals SET stat = NULL WHERE symbol_id = @id`;
  - `UpdateStatBatchAsync`: `UPDATE ... FROM unnest(@ids, @stats)` (JSONB).

## Формат JSON `stat`

Совместим с Python `asdict(StatF)`: `price_open`, `price_stop`, `price_take`,
`result_stop`, `result_take`, `index_open`, `index_stop`, `index_take`,
`index_total`, `has_buy`.

## Запуск

```powershell
.\run_Stat.ps1 -Symbol CNYRUBF -StopPoint 3 -TakePoint 20 [-Clear] [-NoBuild] [-Foreground] [-NoPause]
```

CLI-аргументы: `--symbol`, `--stop-point`, `--take-point`, `--max-deep`,
`--buffer-size`, `--read-chunk-size`, `--clear`.

## Проверка

- `dotnet build C#/src/Stat/Stat.slnx` — успешно (0 ошибок, 0 предупреждений).
- `dotnet test C#/src/Stat/Stat.Tests` — 18 тестов пройдено.
- `dotnet build C#/src/Reader/ArtasChart.sln` и `C#/src/Writer/ArtasWriter.slnx` —
  успешно (регрессий от расширения `StatF` нет).

## Что НЕ переносилось

FinamApi, producers, каналы, Simulation, MT5Client, OpenTelemetry, health-эндпоинты.
