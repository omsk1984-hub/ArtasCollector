# Интеграция с Grafana Loki для Artas2026

## Как запустить сервисы с отправкой логов в Loki

### Шаг 1: Убедитесь что Loki запущен

```bash
cd /mnt/d/work/Artas2026/monitoring
docker-compose ps
```

Должны быть все 2 контейнера: artas_loki, artas_promtail

### Шаг 2: Запустите сервисы

**Вариант A: PowerShell (запускает сервисы в Windows)**

```powershell
# Запуск Writer
.\scripts\manage_fractal_writer.ps1 start CNYRUBF -HealthPort 8080

# Запуск Stat (расчёт статистики)
.\scripts\manage_stat_writer.ps1 CNYRUBF 3 20
```

Логи сервисов пишутся в единый каталог `LOGS_DIR` (по умолчанию `<корень>/logs`,
например `D:\work\Artas2026\logs`): `writer.<port>.stdout.log`,
`writer.<port>.stderr.log` и `stat.<symbol>.log`.
Promtail собирает их и отправляет в Loki — отдельная настройка отправки не требуется.

### Шаг 3: Проверьте логи

```bash
# Через API Loki:
wsl bash -c "curl -s 'http://localhost:3100/loki/api/v1/query?query={job=\"artas_writer\"}'"
```

## Срок хранения логов (retention)

> **Решение:** retention оставлен **единым** — 48 часов для всех уровней.
> Раздельные сроки по уровню (`retention_stream`: ERROR/WARN дольше, DEBUG
> меньше) рассматривались и **отклонены**. Не менять без явного запроса.

Loki хранит логи **не более 2 суток** (`retention_period: 48h`). Очистка
выполняется автоматически процессом `compactor`:

- `retention_delete_delay: 1h` — через час после «просрочки» данные удаляются физически;
- `retention_delete_worker_delay: 20m` — воркер сканирует чанки каждые 20 минут.

Политика задаётся в [`monitoring/loki/config.yml`](../monitoring/loki/config.yml)
и подключается к контейнеру Loki через volume + `-config.file`
(см. [`monitoring/docker-compose.monitoring.yml`](../monitoring/docker-compose.monitoring.yml)).

> Конфиги Loki/Promtail монтируются в контейнеры **каталогом** (а не отдельным
> файлом): при mount одного файла Docker Desktop кэширует его содержимое по inode,
> и правки не подхватываются до пересоздания контейнера.

## Многострочные исключения (multiline)

Многострочные .NET-исключения (сообщение + стек `at ...`) в Promtail **сшиваются
в одну запись** стадией `multiline` (см.
[`monitoring/promtail/config.yml`](../monitoring/promtail/config.yml)). Первой
строкой нового лога считается строка уровня Microsoft.Extensions.Logging
(`info: `/`fail: ` и т.п.); строки стека приклеиваются к ней.

Благодаря этому:
- `detected_level` сшитой записи равен реальному уровню (`fail`), а не `unknown`;
- стек можно прочитать/скопировать целиком одним запросом.

> Стадия `multiline` идёт **до** парсинга уровня — иначе regex уровня сработал бы
> на строках стека и уровень снова стал бы `unknown`.

## Метки в Loki

Promtail извлекает из имён файлов и строк логов набор меток:

| Метка | Источник | Пример |
|---|---|---|
| `job` | имя файла | `artas_writer`, `artas_stat` |
| `app` | имя файла | `artas-writer`, `artas-stat` |
| `svc` | имя файла | `writer`, `stat` |
| `log_stream` | имя файла | `stdout`, `stderr` |
| `symbol` | имя файла (`stat.<symbol>.log`) | `CNYRUBF` |
| `lvl` | уровень из строки (`info:`/`warn:`/`fail:`…) | `info`, `warn`, `fail` |

Дополнительно `logger`, `event_id`, `symbol`, `symbol_id`, `batch_id` выносятся в
**structured metadata** (не индексируются — высокая кардинальность).

- `symbol`/`symbol_id` извлекаются из текста логов пайплайна
  (`... symbol=CNYRUBF symbol_id=6 event_type=quote`).
- `batch_id` — из текста логов записи батча
  (`... batch_id=5b6ecefb count=100 ...`).

> Приложение оборачивает обработку в `ILogger.BeginScope` (`symbol`/`symbol_id` в
> `FractalPipeline.RunAsync`, `batch_id` в `FractalWriterService.FlushAsync`) — это
> пригодится при переходе на структурный вывод (OTel). В файл/Plain-text scope
> по умолчанию не рендерится, поэтому ключевые поля дублируются явно в тексте
> сообщения — их и извлекает Promtail.

Значения `lvl` хранятся как есть (`trce`/`dbug`/`info`/`warn`/`fail`/`crit`);
фильтрация и подсчёт по уровням выполняются в LogQL-запросах
(например, `{job=~"artas_writer|artas_stat", lvl=~"fail|crit"}`).

## Путь попадания логов в Loki

Логи сервисов Artas2026 (C#) пишутся через `ILogger<T>` в файлы единого каталога
`LOGS_DIR` (по умолчанию `<корень>/logs`). Их собирает **Promtail**:

1. **Promtail** — контейнер `artas_promtail` читает файлы логов сервисов
   (см. [`monitoring/promtail/config.yml`](../monitoring/promtail/config.yml))
   и отправляет их в Loki с метками `job`/`app`/`svc`/`log_stream`/`lvl`/`symbol`.

Для включения сбора смонтируйте директории логов сервисов в контейнер Promtail
(см. [`monitoring/README.md`](../monitoring/README.md)).

### Подавление шума

Инфраструктурный шум ASP.NET (`Request starting/finished`, `favicon.ico`,
`Executing/Executed endpoint`, `Writing value of type`) отсекается двумя
рубежами: уровнями категорий `Microsoft.AspNetCore.*` в `appsettings.json`
сервисов и drop-стадией Promtail.

## Архитектура логирования

```
C# Services (Writer, Stat)
    |
    v
ILogger<T> --> File handler в LOGS_DIR (<корень>/logs):
               writer.stdout.log / writer.stderr.log
               stat.<symbol>.log
    |
    v
Promtail (сбор /var/log/artas/*.log, метки job/app/svc/log_stream/lvl/symbol)
    |
    |---HTTP---> Loki :3100
                      |
                      v
                Loki API (curl / LogQL)
```

## Диагностика

Если логи не попадают в Loki:

1. **Проверьте доступность Loki**
   ```bash
   curl http://localhost:3100/-/healthy
   ```

2. **Проверьте логи сервиса** (каталог из `LOGS_DIR`, по умолчанию `<корень>/logs`)
   ```powershell
   Get-Content D:\work\Artas2026\logs\writer.stdout.log -Tail 50
   ```

3. **Проверьте логи Promtail**
   ```bash
   docker compose -f monitoring/docker-compose.monitoring.yml logs promtail
   ```

4. **Проверьте запросом к Loki API**
   ```bash
   curl "http://localhost:3100/loki/api/v1/query_range?query={job=%22artas_writer%22}&limit=10"
   ```

5. **Проверьте метки** (должны присутствовать `lvl`, `log_stream`, `svc`):
   ```bash
   curl http://localhost:3100/loki/api/v1/labels
   curl http://localhost:3100/loki/api/v1/label/lvl/values
   ```

> Если после правки `config.yml` Promtail не подхватывает изменения —
> пересоздайте контейнер: `docker compose up -d --force-recreate promtail`.
> При монтировании каталогом правки применяются после reload.
