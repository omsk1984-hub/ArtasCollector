# Stat (.NET 10)

Batch-сервис расчёта статистики фракталов. Перенесён с Python-сервиса
(`StatService` + `StatCursor`) на C# (.NET 10); отдельного Python-проекта в
репозитории нет.

## Назначение

Сервис **не является вечным стримером**: он читает фракталы из PostgreSQL порциями,
прогоняет `StatCursor` по каждому, записывает результат в JSONB-поле `stat` и завершается.

- **Exit code 0** — прогон успешен.
- **Exit code 1** — ошибка/отмена.

## Архитектура

| Проект | Назначение |
|--------|-----------|
| `Stat.Application` | Чистое ядро: `StatCursor`, `CircularBuffer`, `FractalBufferService`, `StatOptions`, `IStatWalker`. |
| `Stat.Infrastructure` | Доступ к БД: `FractalReadRepository` (чтение), `StatRepository` (запись stat), `StatWalker`, `StatModule` (DI). |
| `Stat.Worker` | Консольный хост: `Program`, `DependencyInjection`, `StatRunner`. |
| `Stat.Tests` | xUnit-тесты `StatCursor` и семантики буфера. |

Используются модели и абстракции `ArtasCore` напрямую (`Fractal`, `StatF`, `MyTick`, `SymbolInfo`, `DbConfig`,
`IFractalReader`, `IStatRepository`). Отдельный `Stat.Core` не создаётся.

## Логика расчёта (перенос Python)

`StatCursor.init(pick_price, has_buy)`:

```
deal_sign = +1 (buy) | -1 (sell)
price_open = round(pick_price + 1 * point * deal_sign, digits)
price_stop = round(price_open - stop_point * point * deal_sign, digits)
price_take = round(price_open + take_point * point * deal_sign, digits)
```

`StatCursor.check(stat, fractal, index)` устанавливает `index_total`; для покупки:
открытие — если `Price < price_open`; стоп — `Price <= price_stop` (`result_stop = stop_point + 1`);
тейк — `Price >= price_take` (`result_take = take_point`); иначе `result_take = int((Price - price_open) / point)`.
Для продажи знаки инвертированы.

Округление — банковское (`MidpointRounding.ToEven`), как Python `round()`.

## Формат JSON `stat`

Совместим с Python `dataclasses.asdict(StatF)`:
`price_open`, `price_stop`, `price_take`, `result_stop`, `result_take`,
`index_open`, `index_stop`, `index_take`, `index_total`, `has_buy`, `from_start_interest`.

## Запуск

```powershell
# из C#/src/Stat
.\run_Stat.ps1 -Symbol CNYRUBF -StopPoint 3 -TakePoint 20
.\run_Stat.ps1 -NoBuild -Foreground -NoPause
```

Либо напрямую:

```powershell
dotnet run --project Stat.Worker -c Release -- --symbol CNYRUBF --stop-point 3 --take-point 20
```

Параметры (appsettings.json секция `Stat` либо CLI):

| Параметр | CLI | По умолчанию |
|----------|-----|--------------|
| Symbol | `--symbol` | `CNYRUBF` |
| StopPoint | `--stop-point` | `3` |
| TakePoint | `--take-point` | `20` |
| MaxDeep | `--max-deep` | `100` |
| BufferSize | `--buffer-size` | `500` |
| ReadChunkSize | `--read-chunk-size` | `500` |
| Clear | `--clear` | `false` |

## Сборка

```powershell
dotnet build Stat.slnx
```

## Отличия от Python-эталона

- **Batch-job** вместо потока: после обработки всех фракталов процесс завершается с exit code.
- **Без health-эндпоинтов** (не требуется для batch-job); прогресс (`SavedCount/total`) — в логах.
- `StatF` в ArtasCore расширен полями `HasBuy` (JSON `has_buy`) и свойствами `IsOpen`/`IsStop`
  (совместимо с существующим чтением в Reader).
