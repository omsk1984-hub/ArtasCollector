# План: Health-чекер для `run_bokeh_db.py` на отдельном порту (5007)

> **Статус: РЕАЛИЗОВАНО.** Реализован Вариант B (graceful stop через `health_server`).
> Проверено: занятый порт → ERROR в Loki + exit(1); `/health` и `/exit` работают.
> ⚠️ Обнаружен и учтён важный нюанс: `is_port_free()` **не должен** ставить `SO_REUSEADDR`
> (см. Шаг 1) — на Windows с ним `bind()` на занятый порт «успешен» (ложное «свободен»).

## Цель
Поднять рядом с Bokeh-сервером визуализации фракталов из БД ([`scripts/run_bokeh_db.py`](../scripts/run_bokeh_db.py:1))
дополнительный HTTP health-endpoint на **отдельном порту** (по умолчанию `5007`),
используя уже существующий модуль [`HealthServer`](../src/artas2026/services/health_server.py:88).

Если **любой** из портов (Bokeh `5006` или health `5007`) занят — записать ошибку в лог (в т.ч. в Loki)
и **завершить процесс**.

## Почему отдельный порт
- Порт `5006` уже занят самим Bokeh-сервером ([`Server(...)`](../src/artas2026/visualization/bokeh_db.py:100)).
  Второй сервер на том же порту не сможет сделать `bind()` → `OSError: address already in use`.
- Bokeh не отдаёт произвольные маршруты (`/health`) — только свои внутренние + WebSocket `/ws`.
- Проксирование (nginx/Caddy) требует проброса WebSocket-`Upgrade`, правки `allow_websocket_origin`
  ([`bokeh_db.py:100`](../src/artas2026/visualization/bokeh_db.py:100)) и внешней зависимости —
  для health-чекера это избыточно. Отдельный порт проще и надёжнее.

## Файлы
| Файл | Действие |
|------|----------|
| [`scripts/run_bokeh_db.py`](../scripts/run_bokeh_db.py:1) | **изменить** — добавить `--health-port`, настройку логирования, запуск health-сервера, проверку портов |
| [`src/artas2026/services/health_server.py`](../src/artas2026/services/health_server.py:88) | **не трогаем** — используем как есть |
| [`src/artas2026/visualization/bokeh_db.py`](../src/artas2026/visualization/bokeh_db.py:69) | **изменить** — `main()` принимает `health_server`, graceful shutdown, SIGINT/SIGTERM |
| [`src/artas2026/visualization/bokeh_server.py`](../src/artas2026/visualization/bokeh_server.py:47) | **исправлено попутно** — синтаксическая ошибка (лишняя `)`) в создании `TickChart`, блокировавшая импорт |
| [`scripts/run_bokeh_db.ps1`](../scripts/run_bokeh_db.ps1:1) | **изменить** — добавить `-HealthPort`, освобождение двух портов, вывод URL health/exit |
| [`pyproject.toml`](../pyproject.toml) | **не трогаем** — новых зависимостей нет |

---

## Шаг 1. Проверка занятости портов до старта

Добавить в [`scripts/run_bokeh_db.py`](../scripts/run_bokeh_db.py:1) утилиту проверки свободности порта
(без зависимостей — `socket`):

```python
import socket

def is_port_free(host: str, port: int) -> bool:
    """True, если порт свободен (можно bind).

    ВАЖНО: SO_REUSEADDR здесь НЕ ставим. На Windows с SO_REUSEADDR
    bind() может «успешно» выполниться на уже занятом порту, что
    даёт ложное «свободен». Без него bind() корректно падает с OSError.
    """
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        try:
            s.bind((host, port))
            return True
        except OSError:
            return False
```

> ⚠️ **Нюанс Windows (проверено):** с `SO_REUSEADDR` `bind()` на занятый порт
> возвращает успех → проверка ложно считает порт свободным. Поэтому опция
> **не должна** использоваться в `is_port_free`.

Логика в `__main__` **до** запуска чего-либо:

```python
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger("run_bokeh_db")

# Настроить логирование (в т.ч. Loki) — см. Шаг 2
setup_logging(...)

health_port = args.health_port  # по умолчанию 5007

for name, p in (("Bokeh", args.port), ("Health", health_port)):
    if not is_port_free("localhost", p):
        msg = f"Порт {p} ({name}) занят — запуск отменён"
        logger.error(msg)
        raise SystemExit(1)   # процесс завершается
```

> **Важно:** занятость Bokeh-порта проверяем **явно до** создания `Server`, потому что
> `bokeh.server.Server` при занятом порте ведёт себя не всегда очевидно (часть версий
> может «зависнуть»/выбросить неочевидную ошибку). Явная проверка даёт чистый лог + `exit(1)`.

---

## Шаг 2. Логирование ошибок в Loki

Использовать штатный [`setup_logging(...)`](../src/artas2026/utils/logger_config.py:267) из
[`logger_config.py`](../src/artas2026/utils/logger_config.py:1), чтобы ошибка о занятом порте
ушла и в файл, и в Loki:

```python
from artas2026.utils.logger_config import setup_artas_logging

setup_artas_logging(
    level=os.environ.get("LOG_LEVEL", "INFO"),
    log_file=None,                           # файловый вывод отключён — только Loki
    enable_loki=True,
    loki_url=os.environ.get("LOKI_URL", "http://localhost:3100/loki/api/v1/push"),
    loki_level=os.environ.get("LOKI_LEVEL", "ERROR"),
    service_name="bokeh_db",
)
```

- **Только Loki + консоль.** `log_file=None` — файловые логи (`bokeh_db.log`,
  `bokeh_db.error.log`) не создаются и не пишутся.
- `service_name="bokeh_db"` — отдельная метка в Grafana/Loki, отличимая от `fractal_writer`.
- Ошибка `logger.error("Порт ... занят")` попадёт в Loki (уровень ERROR).
- ⚠️ Имя функции — [`setup_artas_logging`](../src/artas2026/utils/logger_config.py:235)
  (не `setup_logging`).
- В PowerShell-обёртке убраны `-RedirectStandardOutput/-RedirectStandardError`
  (файлы `bokeh_db.log`/`bokeh_db.error.log`) — весь вывод идёт в Loki и на консоль.

---

## Шаг 3. Запуск health-сервера

Создать health-сервер **в `__main__`** (не в библиотеке `main()`), чтобы держать всю
«склейку» в скрипте запуска:

```python
from artas2026.services.health_server import HealthServer
from datetime import datetime, timezone

# Разделяемый контейнер: BokehServerDb положит сюда tick_service,
# чтобы health-endpoint читал количество загруженных фракталов.
stats: dict = {}

def _read_fractals_info() -> dict:
    """Статистика по фракталам из tick_service (если доступен)."""
    tick_service = stats.get("tick_service")
    if tick_service is None:
        return {"fractals_loaded": 0, "buffer_size": 0, "tick_service_ready": False}
    fractals = getattr(tick_service, "Fractals", None)
    return {
        "fractals_loaded": getattr(fractals, "Count", 0) if fractals is not None else 0,
        "buffer_size": getattr(fractals, "Size", 0) if fractals is not None else 0,
        "tick_service_ready": True,
    }

_start_ts = datetime.now(timezone.utc)

def build_payload() -> dict:
    return {
        "status": "ok",
        "service": "bokeh_db",
        "server_time": datetime.now(timezone.utc).isoformat(),
        "uptime_sec": (datetime.now(timezone.utc) - _start_ts).total_seconds(),
        "bokeh_port": args.port,
        "health_port": args.health_port,
        "symbol": args.symbol,
        "fractals": _read_fractals_info(),   # количество загруженных фракталов
    }

health_server = HealthServer(
    host="localhost",
    port=health_port,
    get_status=build_payload,
)
health_server.start()
if not health_server.started:
    logger.error(f"Health server не запустился: {health_server.bind_error}")
    raise SystemExit(1)
logger.info(f"Health endpoint: http://localhost:{health_server.port}/health")

# Передаём stats в main(), чтобы BokehServerDb опубликовал tick_service
main(..., health_server=health_server, stats=stats)
```

### Количество фракталов — как оно получается
- `BokehServerDb._create_service()` кладёт ссылку на `tick_service` в `stats["tick_service"]`
  (см. [`bokeh_db.py`](../src/artas2026/visualization/bokeh_db.py:45)).
- `tick_service.Fractals` — это [`ListClass`](../src/artas2026/utils/list_class.py:11):
  - `Count` — **общее** число добавленных фракталов (растёт и при переполнении буфера);
  - `Size` — размер циклического буфера (`buffer_size`).
- Пока клиент не подключился к Bokeh и `BokehServerDb` не создан, `tick_service_ready=false`
  и `fractals_loaded=0` — это ожидаемо (Bokeh создаёт документ при подключении клиента).

- При `bind_error` (порт всё-таки занят, гонка) — лог + `exit(1)`.
- Эндпоинт `/exit` у [`HealthServer`](../src/artas2026/services/health_server.py:33) есть «из коробки»,
  но по умолчанию делает `os._exit(0)`. Для корректного (graceful) завершения передать свой
  `get_exit` — см. **Шаг 4** и **Шаг 4.1**.

---

## Шаг 4. Возврат `Server` из `main()` (graceful stop)

Сейчас [`main()`](../src/artas2026/visualization/bokeh_db.py:69) блокирующе крутит
[`loop.run_forever()`](../src/artas2026/visualization/bokeh_db.py:113) и не отдаёт `server` наружу.
Чтобы health по `/exit` мог корректно остановить Bokeh, а не звать `os._exit`:

**Вариант A (минимальный):** не менять `main()`, использовать дефолтный `os._exit(0)` в health.
Жёстко, но просто; Bokeh-процесс всё равно один.

**Вариант B (рекомендуемый):** расширить `main()`, чтобы он принимал готовый `health_server`
и управлял жизненным циклом:

```python
def main(..., health_server: Optional[HealthServer] = None):
    ...
    def _shutdown():
        if health_server is not None:
            health_server.stop()
        loop.call_soon_threadsafe(server.stop)  # остановит Bokeh
        os._exit(0)                              # завершит process

    if health_server is not None:
        health_server.get_exit = _shutdown
    ...
```

Тогда в [`scripts/run_bokeh_db.py`](../scripts/run_bokeh_db.py:1) health создаётся до `main(...)`
и передаётся аргументом.

> **Итоговое решение — Вариант B (реализовано).** `main()` принимает `health_server`;
> при старте подменяется его `get_exit` на идемпотентный `_graceful_shutdown()`
> (health → Bokeh → `logging.shutdown()` → `os._exit(0)`), а `SIGINT`/`SIGTERM` ведут
> в тот же путь. Функция `_graceful_shutdown` защищена `threading.Lock` от двойного вызова
> и использует таймаут-обёртку `stop()` (5с) с гарантированным выходом в `finally`.

---

## Шаг 4.1. Корректный (graceful) `/exit` — отдельный API

Эндпоинт `/exit` уже реализован в [`_HealthHandler.do_GET`](../src/artas2026/services/health_server.py:31):
он отвечает `200 {"status": "exiting"}` и запускает `exit_callback` в **отдельном daemon-потоке**
([`health_server.py:36-38`](../src/artas2026/services/health_server.py:36)). Это важно: ответ клиенту
успевает уйти до остановки процесса.

### Задача
Заменить дефолтный жёсткий `os._exit(0)` (см. [`_exit_default`](../src/artas2026/services/health_server.py:137))
на **корректную последовательность остановки** с таймаутом и гарантированным выходом.

### Целевая последовательность graceful shutdown
```python
import os, signal, threading, time, logging
logger = logging.getLogger("run_bokeh_db")

def graceful_exit(reason: str = "api /exit") -> None:
    """Корректная остановка: health -> Bokeh -> flush логов -> выход.

    Реализует идемпотентность: повторный вызов (двойной /exit) ничего не ломает.
    """
    lock = getattr(graceful_exit, "_lock", None) or threading.Lock()
    graceful_exit._lock = lock
    if not lock.acquire(blocking=False):
        logger.warning("graceful_exit уже выполняется — повторный вызов проигнорирован")
        return
    try:
        logger.info(f"Graceful shutdown запущен ({reason})")

        # 1) Остановить приём health-запросов (освободить порт 5007).
        try:
            health_server.stop()
        except Exception as e:
            logger.error(f"Ошибка остановки health server: {e}")

        # 2) Остановить Bokeh-сервер и его IOLoop (освободить порт 5006).
        try:
            loop.call_soon_threadsafe(server.stop)
        except Exception as e:
            logger.error(f"Ошибка остановки Bokeh server: {e}")

        # 3) Дать обработчикам дописать логи (Loki flush идёт в фоновом потоке).
        logging.shutdown()          # закрывает все handlers, в т.ч. LokiHTTPHandler
        time.sleep(0.2)             # окно на дозапись batched-логов

        logger.info("Graceful shutdown завершён — выход с кодом 0")
    finally:
        # Гарантированный выход: даже если что-то выше зависло.
        os._exit(0)

health_server.get_exit = lambda: graceful_exit("health API /exit")
```

### Почему именно так
- **Ответ уходит раньше остановки** — [`/exit`](../src/artas2026/services/health_server.py:33) шлёт
  `{"status":"exiting"}` до вызова callback в отдельном потоке, клиент не получит обрыв соединения.
- **Порядок важен:** сначала health (перестаём принимать новые команды), затем Bokeh, затем логи.
- **`logging.shutdown()`** — дожидается закрытия [`LokiHTTPHandler`](../src/artas2026/utils/logger_config.py:52)
  и сбрасывает буфер; без него последние ERROR могут не дойти до Loki при `os._exit`.
- **Идемпотентность** — второй `/exit` (например, из PowerShell-обёртки) не запускает
  вторую остановку и не падает.
- **Таймаут-гарантия** — `finally: os._exit(0)` исключает «зависание» процесса, если
  `server.stop()`/`logging.shutdown()` не завершились.

### Альтернатива: таймаут-обёртка для `server.stop()`
Если `loop.call_soon_threadsafe(server.stop)` не отрабатывает в разумное время, обернуть
в ожидание с таймаутом:
```python
done = threading.Event()
def _stop_and_signal():
    try:
        server.stop()
    finally:
        done.set()
loop.call_soon_threadsafe(_stop_and_signal)
if not done.wait(timeout=5):
    logger.error("Bokeh server.stop() не завершился за 5с — принудительный выход")
```
Тогда `finally: os._exit(0)` отработает после таймаута без блокировки.

### Проверка корректного exit
```powershell
# 1) Запустить сервер, затем запросить остановку:
curl http://localhost:5007/exit
# Ожидаемый ответ:
#   {"status": "exiting"}
# 2) Убедиться, что процесс завершился с кодом 0 и порты 5006/5007 свободны:
netstat -ano | Select-String ":5006|:5007"
# 3) В логе/Loki должны быть строки:
#   "Graceful shutdown запущен (health API /exit)"
#   "Graceful shutdown завершён — выход с кодом 0"
```

---

## Шаг 5. Правки `scripts/run_bokeh_db.ps1`

Добавить параметр и вывод:

```powershell
param(
    ...
    [int]$Port = 5006,
    [int]$HealthPort = 5007,
    ...
)
```

- В блок `Free-Port` добавить освобождение **двух** портов (или отдельный вызов `Free-Port -TargetPort $HealthPort`).
- В `$arguments` добавить `"--health-port", $HealthPort`.
- В финальный вывод добавить:
  ```powershell
  Write-Host "  Health:    http://localhost:$HealthPort/health" -ForegroundColor Cyan
  ```

---

## Шаг 6. Проверка

```powershell
# Запуск
D:\PF\anaconda3\python.exe scripts\run_bokeh_db.py --port 5006 --health-port 5007 `
    --symbol CNYRUBF --hours 30

# Bokeh
#   http://localhost:5006
# Health
curl http://localhost:5007/health
```

Ожидаемый ответ:
```json
{
  "status": "ok",
  "service": "bokeh_db",
  "server_time": "2026-10-06T07:55:00+00:00",
  "uptime_sec": 42.1,
  "bokeh_port": 5006,
  "health_port": 5007,
  "symbol": "CNYRUBF",
  "fractals": {
    "fractals_loaded": 287,
    "buffer_size": 500,
    "tick_service_ready": true
  }
}
```

Проверка сценария «порт занят»:
```powershell
# Запустить дважды подряд без освобождения —
# второй процесс должен записать ERROR в лог/Loki и завершиться с кодом 1.
```

Проверка корректного `/exit`:
```powershell
curl http://localhost:5007/exit      # ожидаем {"status": "exiting"}
netstat -ano | Select-String ":5006|:5007"   # оба порта свободны
```

---

## Критерий готовности
- `curl http://localhost:5007/health` → `200` + JSON со статусом, `uptime_sec`
  и блоком `fractals` (`fractals_loaded` — количество загруженных фракталов,
  `buffer_size`, `tick_service_ready`).
- `curl http://localhost:5007/exit` → `200 {"status": "exiting"}`, процесс завершается с кодом `0`,
  оба порта (`5006`, `5007`) освобождены, в логе/Loki есть строки graceful shutdown.
- Повторный `/exit` не ломает процесс (идемпотентность) и не вызывает двойной остановки.
- Если `5006` или `5007` занят — в лог (файл + Loki) уходит `ERROR`, процесс завершается с кодом `1`.
- Bokeh-график на `5006` работает как раньше (WebSocket не затронут).
- Новых зависимостей в [`pyproject.toml`](../pyproject.toml) нет.

## Риски
- **Гонка проверки порта** — между `is_port_free()` и `bind()` порт может быть занят кем-то ещё.
  Закрывается обработкой `health_server.bind_error` (Шаг 3) и повторной проверкой Bokeh-порта.
- **Жёсткий `os._exit`** — при дефолтном callback обрывает flush Loki-очереди. Решается
  корректным `/exit` (Шаг 4.1): `logging.shutdown()` до выхода.
- **Зависание `server.stop()`** — закрывается таймаут-обёрткой и `finally: os._exit(0)` (Шаг 4.1).
- **Двойной `/exit`** — закрывается идемпотентностью через `threading.Lock` (Шаг 4.1).
- **Health как daemon-поток** — не мешает завершению процесса; при `Ctrl+C` закрывается сам.
- **`Ctrl+C` vs `/exit`** — оба пути должны проходить через одну функцию `graceful_exit()`
  (повесить на `signal.SIGINT`), чтобы освобождать порты и сбрасывать логи одинаково.
