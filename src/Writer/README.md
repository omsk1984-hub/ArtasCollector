# ArtasWriter (.NET 10 + Finam API)

Сервис записи фракталов в PostgreSQL в реальном времени. Аналог Python-проекта
[`artas2026`](../../../src/artas2026), но без MetaTrader 5 и без истории тиков:
источник данных — **Finam Trade API** (gRPC): котировки, стакан, сделки, открытый интерес.

> Требуется .NET 10 SDK. Целевой фреймворк задаётся центрально в `Directory.Build.props`.

## Что делает

- Один процесс обрабатывает **все** инструменты (список — в `appsettings.json`).
- На каждый инструмент: подписка в стриме Finam → персональный канал → пайплайн.
- Единственный writer-консьюмер пишет фракталы в `public.fractals` батчами
  с `ON CONFLICT (id, symbol_id) DO NOTHING`.
- Два HTTP-эндпоинта: `/health` (статус) и `/exit` (graceful shutdown).

## Структура решения

```
ArtasWriter.Core           — модели (Fractal, MyTick, InfoF, InfoMeta, StatF, SymbolInfo)
                             + чистое ядро FractalFactory (порт FFNew) + абстракции
ArtasWriter.Application    — TickService, MarketService, FractalBufferService,
                             FinamProducerFactory, InstrumentPipelineFactory,
                             FractalPipeline, WriterOptions
ArtasWriter.FinamApi       — FinamGrpcClient (реальный gRPC-клиент), FinamProducer,
                             FinamProducerBuilder, SimulationEventSource, FinamOptions, Protos/
ArtasWriter.Infrastructure — SymbolRepository, FractalRepository, FractalWriterService,
                             WriteBatcher, DailyCounter, DbConfig, DbHealthProbe
ArtasWriter.Worker         — host, DependencyInjection (AddWriterModule), WriterHostedService,
                             PipelineOrchestrator, WriterChannelFactory, WriterState,
                             HealthSnapshotBuilder, /health, /exit, appsettings
ArtasWriter.Tests          — unit-тесты (FractalFactory, модели, сервисы, WriteBatcher, DailyCounter)
```

### Слои и абстракции (SOLID)

| Абстракция | Реализация | Назначение |
|------------|------------|------------|
| `IFractalWriter` | `FractalWriterService` | writer-консьюмер (host/health зависят от абстракции) |
| `IDailyCounter` | `DailyCounter` | дневной счётчик (сброс по календарной дате) |
| `IWriterState` | `WriterState` | разделяемое состояние сервиса для health |
| `ISimulatedEventSource` | `SimulationEventSource` | генератор синтетических событий (режим симуляции) |
| `IInstrumentPipelineFactory` | `InstrumentPipelineFactory` | создание `TickService` + `FractalPipeline` |
| `IHealthSnapshotBuilder` | `HealthSnapshotBuilder` | чистая сборка снапшота здоровья |

Композиция графа зависимостей вынесена в
[`DependencyInjection.AddWriterModule`](ArtasWriter.Worker/DependencyInjection.cs);
`Program.cs` — только bootstrap. `WriterHostedService` делегирует оркестрацию
[`PipelineOrchestrator`](ArtasWriter.Worker/PipelineOrchestrator.cs).

## Поток данных

```
Finam gRPC стримы (SubscribeQuote/SubscribeLatestTrades/SubscribeOrderBook)
    → FinamGrpcClient → события IFinamClient
    → FinamProducer → Channel<FinamEvent> → FractalPipeline
    → TickService → FractalFactory → Fractal
    → общий Channel<FractalRecord> → FractalWriterService → public.fractals
```

## Интеграция с Finam (gRPC)

Транспорт — **gRPC Server Streaming** по адресу `https://api.finam.ru:443`.
Proto-схемы взяты из официального репозитория
[`FinamWeb/finam-trade-api`](https://github.com/FinamWeb/finam-trade-api) и лежат в
[`ArtasWriter.FinamApi/Protos`](ArtasWriter.FinamApi/Protos).

- **Авторизация:** секретный ключ (`tapi_sk_...`) обменивается на JWT через
  gRPC `AuthService.Auth` (`POST /v1/sessions`). JWT передаётся в метаданных
  заголовка `Authorization` для всех последующих вызовов.
- **Котировки/сделки:** `MarketDataService.SubscribeQuote` (bid/ask/last/volume/open_interest)
  и `SubscribeLatestTrades` (направление сделки BUY/SELL — необходимо ядру фракталов).
- **Стакан:** `MarketDataService.SubscribeOrderBook` (уровни с `price`, `buy_size`/`sell_size`).
- **Открытый интерес:** приходит в `Quote.open_interest` и `Trade.open_interest`.

> **TLS:** в среде проекта сервер согласуется только по TLS 1.2, поэтому клиент
> явно ограничивает `EnabledSslProtocols = Tls12`.

### Коды инструментов (ticker@mic)

Инструменты адресуются в формате `{ticker}@{mic}`:

- Фьючерсы MOEX FORTS: `mic = RTSX` (например, `SiZ6@RTSX`);
- Акции MOEX: `mic = MISX` (например, `SBER@MISX`).

В БД `public.symbols.symbol` хранится только тикер (например, `SiZ6`).
MIC добавляется автоматически из `Finam:DefaultMic` (по умолчанию `RTSX`),
либо можно указать полный символ `SiZ6@RTSX` прямо в колонке `symbol`.

## Конфигурация (`appsettings.json`)

- `Writer:Symbols` — список инструментов; `Name` совпадает с `public.symbols.name`
  (например, `Si`, `CNYRUBF`).
- Разрешение имени: `public.symbols.name` (настройки) → `public.symbols.symbol` (БД,
  код контракта, например `SiZ6`) → `{symbol}@{DefaultMic}` → подписка в Finam.
- `Finam:Token` — **секретный ключ** Finam (`tapi_sk_...`). **Не коммитить!**
  Задавайте через переменную окружения `Finam__Token`.
- `Finam:DefaultMic` — MIC биржи по умолчанию (`RTSX` для фьючерсов MOEX FORTS).
- `Finam:StreamMaxRetries` — лимит последовательных неудачных переподключений
  одного стрима (по умолчанию `3`). По исчерпании лимита проблемный стрим
  останавливается, остальные продолжают работать.
- `Finam:ReauthOnRetry` — при сбое стрима инициировать обновление JWT (auth-keepalive).
- `Finam:AuthRefreshMinutes` — период планового обновления JWT фоновым auth-keepalive.
- `Finam:SimulationMode` — генерация синтетических событий без реального подключения.

## Политика переподключения и авторизация

- **Единая авторизация на клиент.** `FinamGrpcClient` — singleton на процесс, JWT
  (`_token`) общий для всех символов и стримов. Обновлением JWT заведует фоновый
  **auth-keepalive**: он выполняет ровно один `Auth` за раз (под `SemaphoreSlim`) по
  таймауту `AuthRefreshMinutes` и немедленно по сигналу стрима при `Unauthenticated`.
- **Независимые стримы.** На каждый инструмент открываются три потока (котировки,
  стакан, сделки), каждый — отдельная задача с собственным счётчиком попыток.
- **Лимит ретраев = 3.** При обрыве (`RpcException`) счётчик стрима увеличивается; при
  `Unauthenticated`/`PermissionDenied` будится auth-keepalive. После 3 неудач подряд
  **останавливается только проблемный стрим**, остальные продолжают работать, Writer жив.

## Health: статус соединения по символам

`GET /health` содержит `finam_connected` на верхнем уровне (агрегат: `false`, если упал
хотя бы один стрим) и **внутри каждого элемента `symbols[]`** — первым полем блока символа.
Рядом выводится список `streams[]` со статусом каждого потока и причиной остановки:

```json
{
  "finam_connected": false,
  "symbol": "Si",
  "name": "SiZ6",
  "streams": [
    { "kind": "quotes",    "up": true,  "reason": null },
    { "kind": "orderbook", "up": true,  "reason": null },
    { "kind": "trades",    "up": false, "reason": "Invalid access key" }
  ],
  "symbol_id": 1
}
```

Общий `status` = `degraded`, если БД недоступна или есть упавший стрим Finam.

## Запуск

```powershell
# Токен через переменную окружения (рекомендуется)
$env:Finam__Token = "tapi_sk_..."; .\run_Writer.ps1

# Сборка + запуск
./run_Writer.ps1

# Режим симуляции (без Finam)
./run_Writer.ps1 -Simulate

# Проверка эндпоинтов
curl http://localhost:8080/health
curl http://localhost:8080/exit
```

## Тесты

```powershell
dotnet test ArtasWriter.Tests\ArtasWriter.Tests.csproj
```

## Статус реализации

- ✅ Модели Domain 1:1 с Python.
- ✅ Чистое ядро `FractalFactory` (порт `FFNew`) + тесты.
- ✅ Application-слой (событийная обработка, фабрика продьюсеров, пайплайн).
- ✅ Infrastructure (репозитории, единственный writer-консьюмер с батчингом).
- ✅ Worker с `/health` и `/exit`.
- ✅ Режим симуляции для сквозной проверки конвейера.
- ✅ **Реальный gRPC-клиент Finam** (`FinamGrpcClient`): авторизация, стримы
  котировок/сделок/стакана, открытый интерес. Проверено на живых данных
  `SiZ6@RTSX`: тики обрабатываются, фракталы пишутся в `public.fractals`.
- ✅ **Рефакторинг по SOLID**: абстракции `IFractalWriter`, `IWriterState`,
  `IDailyCounter`, `ISimulatedEventSource`, `IInstrumentPipelineFactory`,
  `IHealthSnapshotBuilder`; оркестрация вынесена в `PipelineOrchestrator`;
  композиция — в `AddWriterModule`. Сборка без предупреждений, тесты зелёные.
- ⏳ Integration-тесты `FractalRepository` на реальной БД.

## Связанные документы

- План миграции: [`plans/finam-writer-csharp-migration-plan.md`](../../../plans/finam-writer-csharp-migration-plan.md)
- Базовая миграция: [`C#/src/Writer/fractals-migration-plan.md`](fractals-migration-plan.md)
