# Monitoring Stack для Artas2026 в WSL

Стек мониторинга на основе **Grafana Loki** для сбора и хранения логов проекта Artas2026, запущенный в **WSL (Windows Subsystem for Linux)**. Просмотр логов — через Loki API (http://localhost:3100).

## Быстрый старт в WSL

### 1. Запуск стека мониторинга

```bash
# Из корня проекта в WSL
cd /mnt/d/work/Artas2026

# Сделайте скрипт исполняемым (один раз)
chmod +x scripts/manage_monitoring.sh

# Запуск через bash скрипт
./scripts/manage_monitoring.sh start

# Или напрямую через Docker Compose
cd monitoring
docker compose -f docker-compose.monitoring.yml up -d
```

### 2. Проверка доступности

| Сервис | URL | Порт |
|--------|-----|------|
| Loki API | http://localhost:3100 | 3100 |

- **Loki**: `admin` / (нет пароля, auth_enabled: false)

### 3. Управление сервисами

```bash
# Статус
./scripts/manage_monitoring.sh status

# Логи
./scripts/manage_monitoring.sh logs loki
./scripts/manage_monitoring.sh logs promtail

# Перезапуск
./scripts/manage_monitoring.sh restart

# Остановка
./scripts/manage_monitoring.sh stop

# Полная очистка (удаление данных!)
./scripts/manage_monitoring.sh cleanup

# Справка
./scripts/manage_monitoring.sh help
```

### 4. Прямые команды Docker Compose

```bash
cd monitoring

# Запуск/остановка/статус
docker compose -f docker-compose.monitoring.yml up -d
docker compose -f docker-compose.monitoring.yml down
docker compose -f docker-compose.monitoring.yml ps

# Логи
docker compose -f docker-compose.monitoring.yml logs -f
docker compose -f docker-compose.monitoring.yml logs -f --tail=100 loki

# Обновление образов
docker compose -f docker-compose.monitoring.yml pull
docker compose -f docker-compose.monitoring.yml up -d

# Полная очистка с удалением данных
docker compose -f docker-compose.monitoring.yml down -v
```

## Архитектура

```
┌─────────────────────────────────────────────┐
│              WSL2 Environment                │
│                                              │
│  ┌──────────┐    ┌──────────┐               │
│  │ Artas2026│───>│   Loki   │               │
│  │   (C#)   │ HTTP│ :3100    │               │
│  └──────────┘    └──────────┘               │
│                        ↑                     │
│                ┌───────┴───────┐            │
│                │    Promtail   │            │
│                │ (сбор логов)  │            │
│                └───────────────┘            │
└─────────────────────────────────────────────┘
           ↓ порты проброшены в Windows
┌─────────────────────────────────────────────┐
│          Windows Browser / curl             │
│                                             │
│  http://localhost:3100 → Loki API           │
└─────────────────────────────────────────────┘
```

### Компоненты

| Компонент | Описание | Порт |
|-----------|----------|------|
| **Loki** | Log aggregation system | 3100 |
| **Promtail** | Agent для сбора логов с файловой системы | 9080 |

## Политика хранения логов (retention)

> **Решение:** retention оставлен **единым** — 48 часов для всех уровней.
> Раздельные сроки по уровню (`retention_stream`) рассматривались и
> **отклонены**. Не менять без явного запроса.

Loki хранит логи **не более 2 суток**. Политика задаётся в
[`loki/config.yml`](loki/config.yml) в блоке `compactor`:

| Параметр | Значение | Смысл |
|----------|----------|-------|
| `retention_period` | `48h` | Логи старше 2 дней удаляются |
| `retention_delete_delay` | `1h` | Задержка перед физическим удалением после «просрочки» |
| `retention_delete_worker_delay` | `20m` | Период сканирования чанков воркером compactor |

Конфиг монтируется в контейнер Loki как `/etc/loki/config.yml` и передаётся
флагом `-config.file` (см. [`docker-compose.monitoring.yml`](docker-compose.monitoring.yml)).
Без монтирования конфига Loki работает на встроенных дефолтах, где retention
**выключен**, и логи не удаляются.

Проверить действующую политику:

```bash
curl -s http://localhost:3100/config | grep -A5 compactor
```

## Настройка логирования

Сервисы Artas2026 (C#) пишут структурированные логи через `ILogger<T>` в единый
каталог `LOGS_DIR` (по умолчанию `<корень>/logs`, например
`D:\work\Artas2026\logs`): `writer.stdout.log`, `writer.stderr.log`
и `stat.<symbol>.log`. Promtail читает `/var/log/artas/*.log` и отправляет их в
Loki, извлекая метки regex-стадиями:

| Метка | Источник | Пример |
|---|---|---|
| `job` / `app` / `svc` | имя файла | `artas_writer` / `artas-writer` / `writer` |
| `log_stream` | имя файла | `stdout`, `stderr` |
| `symbol` | `stat.<symbol>.log` | `CNYRUBF` |
| `lvl` | уровень в строке (`info:`…) | `info`, `warn`, `fail` |

`logger`/`event_id`/`symbol`/`symbol_id`/`batch_id` — в structured metadata.
Значения `lvl` хранятся как есть (`trce`/`dbug`/`info`/`warn`/`fail`/`crit`);
фильтрация по уровню — в LogQL-запросах.

### Многострочные исключения

Многострочные .NET-исключения сшиваются в одну запись стадией `multiline`
(первая строка лога — уровень `info:`/`fail:` и т.п.). Благодаря этому
`detected_level` сшитого стека равен реальному уровню, а не `unknown`.
Стадия идёт **до** парсинга `lvl`, иначе regex уровня срабатывал бы на строках стека.

### Настройка через .env

Переменные задаются в корневом `.env` (подключается к compose через `env_file`):

```bash
# Каталог логов сервисов (монтируется в Promtail как /var/log/artas)
LOGS_DIR=D:\work\Artas2026\logs
```

Пути сбора логов задаются в [`monitoring/promtail/config.yml`](promtail/config.yml).
Каталог монтируется в контейнер Promtail в
[`docker-compose.monitoring.yml`](docker-compose.monitoring.yml) как
`../logs:/var/log/artas:ro`.

## LogQL запросы

### Базовые

```logql
# Все логи приложения
{job=~"artas_writer|artas_stat"}

# Логи конкретного сервиса
{job="artas_writer"}
{job="artas_stat"}

# Только ошибки (lvl=fail) и критичные (lvl=crit)
{job=~"artas_writer|artas_stat", lvl=~"fail|crit"}

# Логи конкретного символа (Stat)
{job="artas_stat", symbol="CNYRUBF"}
```

### Продвинутые

```logql
# Подсчёт логов по уровням
sum by (lvl) (count_over_time({job=~"artas_writer|artas_stat"} | lvl != "" [5m]))

# Топ модулей по количеству логов (logger — structured metadata)
topk(10, sum by (logger) (count_over_time({job=~"artas_writer|artas_stat"} | logger != "" [5m])))

# Ошибки за период по модулям
sum by (logger) (count_over_time({job=~"artas_writer", lvl=~"fail|crit"}[$__range]))

# Ошибки конкретного инструмента (symbol/symbol_id — structured metadata)
{job="artas_writer", lvl=~"fail|crit"} | symbol_id=6

# Все логи одного батча записи (batch_id — structured metadata)
{job="artas_writer"} | batch_id="5b6ecefb"

# Поиск по symbol как по тексту (в т.ч. сшитые исключения)
{job="artas_writer"} |= "symbol=CNYRUBF"
```

## Структура файлов

```
monitoring/
├── docker-compose.monitoring.yml   # Docker Compose конфигурация
├── README.md                       # Этот файл
├── loki/
│   ├── config.yml                  # Конфигурация Loki
│   └── data/                       # Данные Loki (volume)
├── promtail/
│   └── config.yml                  # Конфигурация Promtail (читает /var/log/artas/*.log)
├── prometheus/
│   └── prometheus.yml              # Конфигурация Prometheus (метрики памяти)
└── otel-collector/
    └── config.yml                  # OpenTelemetry Collector
```

Каталог логов сервисов (`LOGS_DIR`, по умолчанию `<корень>/logs`) монтируется в
контейнер Promtail как `/var/log/artas` (см. `docker-compose.monitoring.yml`).
```

## Тестирование

Проверка сбора логов — через запросы к Loki API (см. LogQL выше), например:

```bash
curl "http://localhost:3100/loki/api/v1/query_range?query={job=%22artas_writer%22}&limit=10"
```

## Автозапуск при старте WSL

Добавьте в `~/.bashrc`:

```bash
# Auto-start Artas2026 monitoring stack
if pgrep -f artas_loki > /dev/null 2>&1; then
    echo "Monitoring stack is already running"
else
    echo "Starting Artas2026 monitoring stack..."
    cd /mnt/d/work/Artas2026
    ./scripts/manage_monitoring.sh start > /dev/null 2>&1 &
fi
```

## Решение проблем

### Loki не получает логи

1. Проверьте что Promtail запущен: `docker compose logs promtail`
2. Проверьте подключение к Loki: `curl http://localhost:3100/ready`
3. Проверьте пути монтирования в `promtail/config.yml`

### Порты уже заняты

```bash
# Проверить что использует порт
sudo lsof -i :3100

# Изменить порты в docker-compose.monitoring.yml
```

### Высокое потребление памяти

1. Уменьшите `chunk_retain_period` в Loki config
2. Используйте `limit` в LogQL запросах

## План развёртывания

Полный план см. в [`plans/loki-grafana-deployment-plan.md`](../../plans/loki-grafana-deployment-plan.md)
