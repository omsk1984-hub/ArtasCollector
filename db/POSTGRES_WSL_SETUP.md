# Развертывание PostgreSQL в Docker (WSL)

## Запуск PostgreSQL

После развертывания PostgreSQL в Docker внутри WSL контейнер управляется через `docker compose`:

```powershell
wsl bash -c "cd /mnt/d/work/Artas2026/db && docker compose up -d postgres"
```

### Проверка статуса

```powershell
wsl docker ps --filter name=artas_postgres --format "{{.Names}} {{.Status}}"
```

Ожидаемый вывод: `artas_postgres Up N (healthy)`.

> При старте WSL-ВМ контейнер поднимается автоматически сервисом
> `wsl-artas-autostart.service` (см. раздел ниже). Если базы нет в списке —
> запустите вручную командой выше.

### Проверка порта (5432)

```powershell
wsl bash -c "sudo ss -tlnp | grep 5432"
```

Если порт прослушивается — PostgreSQL готов принимать подключения.

---

## Корректное завершение при выключении/перезагрузке Windows

### Проблема

При перезагрузке Windows WSL-ВМ гасится, и если Docker/PostgreSQL не успевают
остановиться корректно, журнал (WAL) остаётся в «грязном» состоянии. При
следующем старте PostgreSQL пытается выполнить recovery, что может выглядеть как
«проблемы с журналом» и циклические перезапуски.

### Решение: два systemd-сервиса внутри WSL

Установлены **два** парных сервиса:

1. **`wsl-docker-graceful-shutdown.service`** — при остановке ВМ (halt/reboot/shutdown)
   выполняет `docker stop -t 60 artas_postgres`. PostgreSQL успевает сделать
   checkpoint и корректно закрыть WAL.
2. **`wsl-artas-autostart.service`** — при загрузке ВМ поднимает `artas_postgres`.
   Это необходимо потому, что при выключении используется `docker stop`, и Docker
   считает остановку «ручной» — `restart: unless-stopped` не поднимает контейнер сам.

Установка/проверка:

```powershell
# Установить оба сервиса
.\scripts\install_wsl_graceful_shutdown.ps1

# Проверить статус
.\scripts\install_wsl_graceful_shutdown.ps1 -Status

# Удалить
.\scripts\install_wsl_graceful_shutdown.ps1 -Uninstall
```

Файлы:
- [`db/init-scripts/wsl-docker-graceful-shutdown.service`](db/init-scripts/wsl-docker-graceful-shutdown.service)
- [`db/init-scripts/artas-graceful-stop.sh`](db/init-scripts/artas-graceful-stop.sh)
- [`db/init-scripts/wsl-artas-autostart.service`](db/init-scripts/wsl-artas-autostart.service)
- [`db/init-scripts/artas-autostart.sh`](db/init-scripts/artas-autostart.sh)

Логи: `/var/log/artas-graceful-stop.log`, `/var/log/artas-autostart.log` внутри WSL.

> ВАЖНО: в unit-файле НЕ используется `Conflicts=shutdown.target`. Этот параметр
> вызывал ложные срабатывания `ExecStop` при каждом обращении к WSL, из-за чего
> PostgreSQL останавливался сам по себе. Достаточно `Before=shutdown.target`.

### Решение (дополнительно): Windows-задача при выключении

Задача `Artas_SafeShutdown` срабатывает на событие `System EventID=1074`
(инициировано плановое завершение работы) и дополнительно останавливает PG до
того, как Windows начнёт гасить WSL.

```powershell
# Зарегистрировать/обновить задачу
.\scripts\register_shutdown_task.ps1

# Проверить
.\scripts\register_shutdown_task.ps1 -Status

# Удалить
.\scripts\register_shutdown_task.ps1 -Unregister
```

### Конфигурация `.wslconfig`

Файл `C:\Users\<пользователь>\.wslconfig` задаёт ресурсы WSL-ВМ. Шаблон:
[`.wslconfig.template`](.wslconfig.template).

---

## Если PostgreSQL не восстановился после перезагрузки

1. **Сначала диагностика, без разрушительных действий:**
   ```powershell
   .\scripts\fix_wal_corruption.ps1 -DryRun
   .\scripts\fix_wal_corruption.ps1
   ```
   Скрипт поднимет отсутствующий контейнер (`docker compose up -d`), проверит
   логи, health и выполнит мягкий `docker compose restart` (данные сохраняются).
   Данные НЕ удаляются без флага `-ForceReset` и подтверждения словом `DELETE`.
   Этот скрипт объединяет прежнюю логику `fix_pg_after_shutdown.ps1`.

2. **Проверка данных:**
   ```powershell
   wsl docker exec artas_postgres psql -U artas_user -d artas_db -tAc "SELECT count(*) FROM fractals;"
   wsl docker exec artas_postgres psql -U artas_user -d artas_db -tAc "SELECT pg_is_in_recovery();"
   ```
   `pg_is_in_recovery() = f` и ненулевой count — база здорова.

3. **Если база действительно повреждена** — восстановление из бэкапа:
   ```powershell
   .\scripts\auto_backup_pg.ps1   # создать свежий бэкап (если база работает)
   ```

---

## Поведение WSL при выходе последнего пользователя (логаут)

### Симптом

«WSL перезагружается», когда выходят все пользователи: `wsl -d Ubuntu -e uptime`
показывает `up 0 min` при **каждом** вызове, контейнеры в состоянии
`Up N seconds (health: starting)`.

### Причина (архитектурное поведение WSL2, не сбой)

WSL **сам инициирует выключение дистрибутива**, когда закрывается **последний клиент
`wsl.exe`** (терминал, сеанс). В journal видно:

```
Operation canceled @p9io.cpp:258 (AcceptAsync)   <- WSL Plan9 teardown
systemd-logind[215]: System is powering down.    <- гасит всю систему
systemd-logind[215]: Session 1 logged out.
```

Ключевой факт: **дистрибутив WSL живёт, пока есть активный клиент `wsl.exe` в текущей
интерактивной сессии**, а не «пока есть процессы». Фоновые процессы внутри ВМ
(`sleep infinity`, systemd-сервис) удержать дистрибутив **не могут** — это проверено
экспериментально. Клиент `wsl.exe` из чужой logon-сессии хотя и удерживает дистрибутив,
но **монополизирует его** (интерактивный доступ `wsl -d Ubuntu` даёт «Отказано в доступе»).

Полный разбор экспериментов: [`plans/wsl-restart-on-last-user-logoff.md`](plans/wsl-restart-on-last-user-logoff.md).

### Принятое решение: полагаться на самовосстановление

Специальных мер не требуется — связка сервисов **уже безопасна**:

1. При выключении ВМ [`wsl-docker-graceful-shutdown.service`](db/init-scripts/wsl-docker-graceful-shutdown.service)
   делает `docker stop -t 60 artas_postgres` → PostgreSQL корректно закрывает WAL.
2. При следующем boot [`wsl-artas-autostart.service`](db/init-scripts/wsl-artas-autostart.service)
   поднимает контейнеры обратно.
3. Задача `AutoStart WSL Ubuntu` (LogonTrigger) поднимает ВМ после входа пользователя.

Издержка — простой контейнеров, пока нет залогиненного пользователя. **Данные в
безопасности, повреждения БД нет** (`RestartCount` не растёт, `pg_is_in_recovery = f`).

> Если нужна непрерывная работа 24/7 без залогиненного пользователя — WSL архитектурно
> не подходит; следует использовать полноценную ВМ (Hyper-V/VMware). См. варианты в
> [`plans/wsl-restart-on-last-user-logoff.md`](plans/wsl-restart-on-last-user-logoff.md).

### Как удержать ВМ (если нужна непрерывная работа при залогиненном пользователе)

Обычный `wsl -d Ubuntu` **занимает терминал** (для выхода — `exit`, что гасит ВМ).
Используйте [`scripts/wsl_hold.ps1`](scripts/wsl_hold.ps1) — он запускает фоновый клиент
`wsl.exe -d Ubuntu -e sleep infinity` через `Start-Process -WindowStyle Hidden`
(окна консоли НЕ создаёт, переживает завершение скрипта):

```powershell
.\scripts\wsl_hold.ps1          # запустить удержание (не занимает окно PowerShell)
.\scripts\wsl_hold.ps1 -Status  # проверить
.\scripts\wsl_hold.ps1 -Stop    # остановить
```

Проверено: окно PowerShell свободно, обычный доступ `wsl -d Ubuntu` и `wsl docker ps`
не блокируется (exit=0), uptime контейнеров накапливается.

**Автозапуск при входе:** удержание добавлено вторым действием в задачу
`AutoStart WSL Ubuntu` (запускается автоматически после входа в Windows).

```powershell
.\scripts\register_wsl_hold_task.ps1            # добавить/обновить автозапуск удержания
.\scripts\register_wsl_hold_task.ps1 -Status    # проверить
.\scripts\register_wsl_hold_task.ps1 -Remove    # убрать
```

> ВАЖНО: задача запускает [`scripts/wsl_hold.ps1`](scripts/wsl_hold.ps1) через
> **`pwsh.exe` (PowerShell 7)**, а не `powershell.exe` (5.1): 5.1 читает `.ps1` без BOM
> как ANSI и падает на кириллице (`Result=1`). Клиент создаётся в интерактивной сессии
> (Principal=Interactive), поэтому не монополизирует дистрибутив.

---

## Диагностика циклических перезапусков

Признак реального сбоя — **рост `RestartCount`**, а не короткий uptime
(контейнер может быть просто только что запущен):

```powershell
wsl docker inspect artas_postgres --format "RestartCount={{.RestartCount}} Health={{.State.Health.Status}}"
```

Если `RestartCount` растёт — смотрите логи:
```powershell
wsl docker logs --tail 100 artas_postgres
```

> ВНИМАНИЕ: старый скрипт [`scripts/monitor_pg_startup.ps1`](scripts/monitor_pg_startup.ps1)
> ранее использовал признак «uptime < 60 сек → пересоздать контейнер». Это создавало
> бесконечный цикл перезапусков. Логика исправлена на проверку `RestartCount`/health.
> Задача `Artas_PgMonitor` в планировщике отключена и не должна включаться.
>
> Скрипт `fix_pg_after_shutdown.ps1` удалён — его функционал объединён в
> [`scripts/fix_wal_corruption.ps1`](scripts/fix_wal_corruption.ps1).
