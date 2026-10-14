# Artas2026 - Система анализа фракталов на финансовых рынках

Проект для анализа фракталов на финансовых рынках. Полностью переведён на **C# (.NET 10)**:
сбор данных через **Finam Trade API** (gRPC), хранение в **PostgreSQL**, визуализация на
**Avalonia + ScottPlot**, расчёт статистики фракталов и сбор логов через стек **Loki + Promtail**.

Docker (PostgreSQL и стек мониторинга) запускается внутри **WSL (Windows Subsystem for Linux)**,
поэтому управление контейнерами осуществляется через WSL-терминал.

## Ключевые возможности

- **Обнаружение фракталов** по потоку тиков (Finam Trade API, gRPC).
- **Запись фракталов в PostgreSQL** с разбиением (партиционированием) таблицы по символам.
- **Health-сервер** для сервиса Writer: JSON-статус (`/health`) и graceful shutdown (`/exit`).
- **Мультисимвольный запуск**: один процесс Writer обрабатывает все инструменты из `appsettings.json`.
- **Расчёт статистики** фракталов (stop/take) отдельным batch-сервисом (`Stat`).
- **Визуализация** фракталов (Avalonia + ScottPlot) с чтением данных из PostgreSQL.
- **Сбор логов и метрик** через Promtail → Loki, Prometheus + OpenTelemetry (см. [`monitoring/README.md`](monitoring/README.md)).
- **Безопасное завершение** системы и автобэкапы PostgreSQL.

## Структура проекта

```
Artas2026/
├── README.md                          # Этот файл
├── .env.example                       # Пример переменных окружения
├── .gitignore                         # Игнорируемые файлы
│
├── src/                               # Исходный код C# (.NET 10)
│   ├── Core/                          # Общая библиотека
│   │   └── ArtasCore/                 # Модели, JSON-конвертеры, EF Core DbContext, абстракции
│   │       ├── Models/                # MyTick, Fractal, InfoF, InfoMeta, StatF, TickFlag, ...
│   │       ├── Json/                  # FlexibleNumberConverters
│   │       ├── Data/                  # DbConfig, FractalMetadataDto, ArtasDbContext,
│   │       │                          #   Entities/ и EF Core Migrations/
│   │       └── Abstractions/          # IFractalReader, IFractalRepository, IHealthProbe,
│   │                                  #   IStatRepository, ISingleInstanceGuard
│   │
│   ├── Writer/                        # Сервис записи фракталов (Finam API → PostgreSQL)
│   │   ├── ArtasWriter.slnx
│   │   ├── ArtasWriter.Core/          # Модели и абстракции Writer
│   │   ├── ArtasWriter.Application/   # Сервисы, пайплайн, продьюсеры
│   │   ├── ArtasWriter.FinamApi/      # gRPC-клиент Finam, симуляция
│   │   ├── ArtasWriter.Infrastructure/# Репозитории PG, батчинг записи, health-probe
│   │   ├── ArtasWriter.Worker/        # Точка входа, DI, health-эндпоинты
│   │   ├── ArtasWriter.Tests/         # Unit-тесты
│   │   └── run_Writer.ps1             # Лаунчер Writer
│   │
│   ├── Stat/                          # Batch-сервис расчёта статистики фракталов
│   │   ├── Stat.slnx
│   │   ├── Stat.Application/          # StatCursor, буферы, опции
│   │   ├── Stat.Infrastructure/       # Чтение фракталов, запись статистики
│   │   ├── Stat.Worker/               # Точка входа, DI
│   │   ├── Stat.Tests/                # Unit-тесты
│   │   └── run_Stat.ps1               # Лаунчер Stat
│   │
│   ├── Reader/                        # Визуализация фракталов (Avalonia + ScottPlot)
│   │   ├── ArtasChart.sln
│   │   ├── ArtasChart.App/            # Avalonia-приложение (View/ViewModel)
│   │   ├── ArtasChart.Services/       # Фильтры, подготовка серий, форматирование
│   │   ├── ArtasChart.Data/           # Репозиторий чтения фракталов
│   │   └── run_Reader.ps1             # Лаунчер Reader
│   │
│   ├── Tools/                         # Диагностические утилиты (MemReport, ReportAnalyzer)
│   │   └── ArtasTools.slnx
│   │
│   └── Go/                            # Мини-пример на Go (не часть системы)
│
├── scripts/                           # Управляющие скрипты (PowerShell)
│   ├── auto_start_postgres.ps1        # Автозапуск PostgreSQL
│   ├── auto_backup_pg.ps1             # Автобэкап PostgreSQL
│   ├── graceful_pg_shutdown.ps1       # Graceful остановка PostgreSQL
│   ├── fix_wal_corruption.ps1         # Диагностика/восстановление PG
│   ├── monitor_pg_startup.ps1         # Мониторинг старта PostgreSQL
│   ├── register_shutdown_task.ps1     # Регистрация задачи завершения
│   ├── register_wsl_hold_task.ps1     # Регистрация WSL hold-задачи
│   ├── install_wsl_graceful_shutdown.ps1 # Установка graceful shutdown WSL
│   ├── cleanup_docker_wsl.ps1         # Очистка Docker в WSL
│   ├── load_docker_images.ps1         # Загрузка образов Docker
│   ├── save_docker_images.ps1         # Сохранение образов Docker
│   ├── kill_by_ports.ps1              # Освобождение занятых портов
│   ├── memory_leak_report.ps1         # Отчёт об утечке памяти (Prometheus)
│   ├── safe_shutdown.ps1              # Безопасное завершение системы
│   ├── stop_all_and_shutdown.ps1      # Остановка всего и завершение
│   ├── wsl_safe_shutdown.ps1          # Безопасное завершение WSL
│   ├── wsl_disk_usage.ps1             # Использование диска WSL
│   ├── wsl_hold.ps1                   # Удержание WSL от завершения
│   ├── manage_monitoring.ps1/.sh/.bat # Управление стеком мониторинга
│   └── SERVICE_MANAGEMENT.md          # Документация управления сервисами
│
├── db/                                # Конфигурация базы данных (Docker)
│   ├── docker-compose.yml             # PostgreSQL (схема — EF Core миграциями)
│   ├── Dockerfile                     # Образ PostgreSQL с кастомной конфигурацией
│   ├── apply-migrations.ps1           # Применение EF Core миграций
│   ├── init-scripts/                  # Конфиг и сервисные скрипты PG (WSL)
│   ├── update.ps1                     # Скрипт обновления БД
│   ├── scripts.sql                    # SQL-утилиты
│   ├── database_schema.md             # Схема БД
│   └── README.md
│
├── monitoring/                        # Стек мониторинга (Loki + Promtail + Prometheus + OTel)
│   ├── docker-compose.monitoring.yml
│   ├── loki/config.yml
│   ├── promtail/config.yml
│   ├── prometheus/prometheus.yml
│   ├── otel-collector/config.yml
│   └── README.md
│
├── deploy/                            # Развёртывание стека мониторинга в WSL
│   └── README.md
│
├── docs/                              # Документация
│   ├── running-project.md             # Пошаговый запуск проекта
│   ├── adding-symbols.md              # Добавление новых символов
│   ├── data_flow_from_start_to_pg.md  # Путь данных: от старта до PostgreSQL
│   ├── loki_integration.md
│   ├── model.md
│   ├── postgres_connection_issue.md
│   ├── powershell-nested-cyrillic-encoding.md
│   └── wsl-restart-on-last-user-logoff.md
│
├── plans/                             # Планы и задачи (markdown)
└── logs/                              # Логи приложения (читаются Promtail)
```

## Установка и настройка

### 1. Клонирование репозитория
```bash
git clone <repository-url>
cd Artas2026
```

### 2. Установка .NET SDK
Установите **.NET 10 SDK**: https://aka.ms/dotnet/download

Сборка решений:
```powershell
dotnet build src/Core/ArtasCore/ArtasCore.csproj
dotnet build src/Writer/ArtasWriter.slnx
dotnet build src/Stat/Stat.slnx
dotnet build src/Reader/ArtasChart.sln
```

### 3. Настройка переменных окружения
Скопируйте файл `.env.example` в `.env` и настройте параметры:
```powershell
Copy-Item .env.example .env
# Отредактируйте .env под вашу конфигурацию
```

Основные переменные:

| Переменная | Назначение | Значение по умолчанию |
|------------|------------|-----------------------|
| `DB_HOST` / `DB_PORT` | Адрес и порт PostgreSQL | `localhost` / `5432` |
| `DB_NAME` / `DB_USER` / `DB_PASSWORD` | Доступ к БД | `artas_db` / `artas_user` / `artas_password` |
| `Finam__Token` | Токен Finam Trade API | — |

Параметры сервисов задаются в `appsettings.json` соответствующего воркера
([`ArtasWriter.Worker`](src/Writer/ArtasWriter.Worker/appsettings.example.json),
[`Stat.Worker`](src/Stat/Stat.Worker/appsettings.example.json),
[`ArtasChart`](src/Reader/appsettings.example.json)).

### 4. Запуск базы данных (Docker в WSL)

Docker работает внутри **WSL**, поэтому контейнеры запускаются через WSL-терминал:
```bash
wsl bash -c "cd /mnt/d/work/Artas2026/db && docker compose up -d"
```

`docker-compose.yml` поднимает сервис:
- **postgres** — PostgreSQL (порт пробрасывается только на `127.0.0.1:5432`), с healthcheck.

Схема БД управляется EF Core миграциями (проект [`ArtasCore`](src/Core/ArtasCore)),
применение — отдельным шагом через [`db/apply-migrations.ps1`](db/apply-migrations.ps1).
Контейнера-мигратора в compose нет: применяются миграции явной командой
(см. [`db/README.md`](db/README.md)).

### 5. Запуск стека мониторинга (опционально)

Loki + Promtail поднимаются из каталога `monitoring`. Подробности — в
[`monitoring/README.md`](monitoring/README.md) и [`deploy/README.md`](deploy/README.md).

```bash
wsl bash -c "cd /mnt/d/work/Artas2026 && ./scripts/manage_monitoring.sh start"
```

Доступ:
- Loki API — http://localhost:3100.

## Запуск приложения

> Полная пошаговая инструкция запуска — в [`docs/running-project.md`](docs/running-project.md)
> (подготовка окружения, БД, запуск сервисов и правило перекомпиляции).
> **Перед перекомпиляцией сначала спросите пользователя, нужно ли останавливать
> запущенные сервисы** — работающий процесс держит DLL из `bin` и ломает сборку (`MSB3027`).

### Сервис записи фракталов (Writer)

Один процесс Writer обрабатывает **все** инструменты из `Writer:Symbols`
([`appsettings.json`](src/Writer/ArtasWriter.Worker/appsettings.json)). Health-порт —
`Writer:HealthPort` (по умолчанию `8080`); второй экземпляр на тот же порт запустить
нельзя (порт — защита от двойного запуска).

Запуск через лаунчер (по умолчанию — в фоне):
```powershell
.\src\Writer\run_Writer.ps1                 # build Release + run (фон)
.\src\Writer\run_Writer.ps1 -Foreground     # запуск в текущем терминале
.\src\Writer\run_Writer.ps1 -Simulate       # режим симуляции (без Finam)
.\src\Writer\run_Writer.ps1 -NoBuild        # запуск без пересборки
.\src\Writer\run_Writer.ps1 -Stop           # graceful shutdown (через /exit)
```

Инструменты по умолчанию (один процесс, health-порт `8080`):

| Символ | Источник |
|--------|----------|
| `Si`, `MX`, `IMOEXF`, `CNY`, `USDRUBF`, `CNYRUBF`, `MM`, `RI` | `Writer:Symbols` в `appsettings.json` |

Проверка и остановка через health-эндпоинт:
```powershell
Invoke-RestMethod http://localhost:8080/health
Invoke-RestMethod http://localhost:8080/exit
```

### Сервис расчёта статистики (Stat, stop/take)

Batch-job: полный прогон и завершение (exit code 0 — успех, 1 — ошибка):
```powershell
.\src\Stat\run_Stat.ps1                                  # по умолчанию Symbol=Si
.\src\Stat\run_Stat.ps1 -Symbol CNYRUBF -StopPoint 3 -TakePoint 20
.\src\Stat\run_Stat.ps1 -Clear                           # очистить stat перед прогоном
.\src\Stat\run_Stat.ps1 -NoBuild                         # без пересборки
```

### Визуализация (Reader, Avalonia + ScottPlot)
```powershell
.\src\Reader\run_Reader.ps1
.\src\Reader\run_Reader.ps1 -CheckDb Si     # диагностика подключения к БД без GUI
.\src\Reader\run_Reader.ps1 -Symbol CNYRUBF # GUI для конкретного символа
```

### Безопасное завершение системы
```powershell
.\scripts\safe_shutdown.ps1                 # остановить writer'ы + PostgreSQL
.\scripts\safe_shutdown.ps1 --wsl-shutdown  # дополнительно закрыть WSL
```

## Health-эндпоинты

Каждый сервис записи фракталов поднимает HTTP health-сервер:

- `GET /health` — JSON-статус сервиса;
- `GET /exit` — graceful shutdown процесса.

Пример ответа `/health` сервиса записи:
```json
{
  "status": "ok",
  "server_time": "2026-10-06T10:00:00+00:00",
  "symbols": [
    {
      "symbol": "CNYRUBF",
      "last_tick": { "time_msc": 0, "time": "...", "last": 0, "bid": 0, "ask": 0, "volume": 0, "flags": 0, "interest": 0 },
      "fractals_count_today": 42
    }
  ]
}
```

Проверка через curl:
```bash
curl http://localhost:8080/health
```
или PowerShell:
```powershell
Invoke-RestMethod http://localhost:8080/health
```

## Основные компоненты

### 1. Core (`src/Core/ArtasCore`)
- **`MyTick`** — модель тика (`record`, соответствует frozen dataclass).
- **`Fractal`, `InfoF`, `InfoMeta`, `StatF`, `MarketPair`, `TempPair`, `SymbolInfo`** — доменные модели.
- **`TickFlag`** — типизированный набор битовых флагов тика.
- **`FlexibleNumberConverters`** — гибкие JSON-конвертеры (int/float/string).
- **`DbConfig`, `FractalMetadataDto`** — конфигурация БД и DTO JSONB-метаданных.
- **Абстракции**: `IFractalReader`, `IFractalRepository`, `IHealthProbe`, `IStatRepository`.

### 2. Writer (`src/Writer`)
Сервис записи фракталов в PostgreSQL в реальном времени по событиям Finam Trade API (gRPC):
котировки, стакан, сделки, открытый интерес.
- **`ArtasWriter.Core`** — модели (`FractalFactory` — алгоритм обнаружения фракталов) и абстракции.
- **`ArtasWriter.Application`** — `TickService`, `MarketService`, `FractalBufferService`, пайплайн, фабрики продьюсеров.
- **`ArtasWriter.FinamApi`** — gRPC-клиент Finam, продьюсеры, режим симуляции.
- **`ArtasWriter.Infrastructure`** — репозитории PG, батчинг записи (`WriteBatcher`), дневной счётчик, health-probe.
- **`ArtasWriter.Worker`** — точка входа, DI-композиция, health-эндпоинты, оркестрация пайплайнов.

### 3. Stat (`src/Stat`)
Batch-сервис расчёта статистики фракталов (stop/take):
- **`Stat.Application`** — `StatCursor`, циклический буфер, опции.
- **`Stat.Infrastructure`** — чтение фракталов из БД, `StatWalker`, запись статистики.
- **`Stat.Worker`** — точка входа, DI-композиция.

### 4. Reader (`src/Reader`)
Приложение визуализации фракталов (Avalonia + ScottPlot, MVVM):
- **`ArtasChart.Data`** — репозиторий чтения фракталов из PostgreSQL.
- **`ArtasChart.Services`** — фильтрация, локальные экстремумы, подготовка серий, форматирование.
- **`ArtasChart.App`** — Avalonia UI (View/ViewModel), графики ScottPlot.

## Хранение данных (PostgreSQL)

- Таблица `fractals` **партиционирована по символу**.
- Композитный первичный ключ, мягкое удаление (`delated`), поле статистики (JSONB `stat`).
- Символы хранятся отдельно с человекочитаемым именем (`name`) и параметрами `point`/`digits`.
- Схема БД управляется **EF Core миграциями** (проект [`ArtasCore`](src/Core/ArtasCore));
  применение — отдельной командой [`db/apply-migrations.ps1`](db/apply-migrations.ps1)
  (`dotnet ef database update`). Контейнера-мигратора в compose нет.
- Схема БД описана в [`db/database_schema.md`](db/database_schema.md).

## Мониторинг и логирование

- Логи сервисов пишутся в единый каталог `LOGS_DIR` (`<корень>/logs`, например
  `D:\work\Artas2026\logs`): `writer.stdout.log`, `writer.stderr.log`, `stat.<symbol>.log`.
- **Promtail** собирает логи и отправляет их в **Grafana Loki**; просмотр — через Loki API (http://localhost:3100).
- **Метрики** .NET-рантайма Writer отправляются по OTLP в **OTel Collector** →
  **Prometheus** (http://localhost:9090); отчёты по памяти — утилитами
  [`src/Tools`](src/Tools/README.md).
- Управление стеком: [`scripts/manage_monitoring.sh`](scripts/manage_monitoring.sh) (WSL) / [`scripts/manage_monitoring.ps1`](scripts/manage_monitoring.ps1) (Windows).
- Документация: [`monitoring/README.md`](monitoring/README.md), [`deploy/README.md`](deploy/README.md), [`docs/loki_integration.md`](docs/loki_integration.md).

## Разработка

### Запуск тестов
Тесты запускаются через `dotnet test`:
```powershell
dotnet test src/Writer/ArtasWriter.slnx
dotnet test src/Stat/Stat.slnx
dotnet test src/Reader/ArtasChart.sln
```

## Лицензия

MIT

## Контакты

Команда Artas - team@artas.example.com
