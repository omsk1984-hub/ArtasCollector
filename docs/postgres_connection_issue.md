# Диагностика проблем с подключением к PostgreSQL

## Проблема

При выполнении `SELECT * FROM public.fractals ORDER BY "time" DESC;` из Windows в WSL периодически появляется ошибка:

```
SQL Error [08001]: Connection to localhost:5432 refused. Check that the hostname and port is correct and that the postmaster is accepting TCP/IP connections.
```

## Причина

PostgreSQL запущен в Docker-контейнере `artas_postgres`, который **периодически перезапускается**. При перезапуске порт 5432 временно недоступен, и подключения отклоняются.

### Доказательство из логов контейнера

Из [`db/docker-compose.yml`](db/docker-compose.yml):
```yaml
restart: unless-stopped  # Перезапуск если контейнер упал
```

Из `docker logs artas_postgres`:

| Время (UTC) | Действие | Длительность работы |
|-------------|----------|---------------------|
| 04:44:29 | `received fast shutdown request` | — |
| 04:50:02 | `starting PostgreSQL 16.15` | ~5 мин 33 сек |
| 04:50:18 | `received fast shutdown request` | **~16 секунд** |
| 04:51:02 | `starting PostgreSQL 16.15` | ~16 секунд |
| 04:58:24 | `starting PostgreSQL 16.15` | ~7 минут |
| 04:58:43 | `received fast shutdown request` | ~19 секунд |
| 05:09:40 | `starting PostgreSQL 16.15` | ~11 минут |
| 05:09:56 | `received fast shutdown request` | ~16 секунд |
| 05:26:48 | `starting PostgreSQL 16.15` | Сейчас работает |

## КТО останавливает PostgreSQL?

Все shutdown приходят с сообщением `received fast shutdown request` — это мягкая остановка через SQL-команду `SELECT pg_ctlstop()` или прямой `docker stop`.

Возможные причины:
1. **Другой процесс/скрипт** отправляет `docker stop artas_postgres`
2. **Docker restart policy** срабатывает из-за healthcheck
3. **Systemd/cron в WSL** может отправлять сигналы
4. **VS Code task / launch.json** запускает/останавливает контейнер

## Решения

### Решение 1: Отключить автосохранение healthcheck timeout

Добавить в [`db/docker-compose.yml`](db/docker-compose.yml):
```yaml
healthcheck:
  test: ["CMD-SHELL", "pg_isready -U artas_user -d artas_db"]
  interval: 30s       # Увеличить интервал
  timeout: 10s        # Увеличить таймаут
  retries: 10         # Больше повторных попыток
  start_period: 30s   # Начальный период для старта
```

### Решение 2: Добавить graceful handling в клиентском коде

Добавить retry-логику при переподключении в провайдере `NpgsqlDataSource`
(см. [`ArtasCore`](src/Core/ArtasCore) `DbConfig` и репозитории Writer/Stat).

### Решение 3: Запустить PostgreSQL без автостарта Docker

Заменить `restart: unless-stopped` на `restart: no` и управлять контейнером вручную через скрипт.

### Решение 4: Выявить источник stop-команды

Выполнить мониторинг:
```bash
# В WSL - отслеживать все команды docker
sudo auditctl -w /var/run/docker.sock -p x

# Или просто
watch -n 5 'docker ps -a --format "{{.Names}} {{.Status}}"'
```

## Диагностика

- Проверка подключения из Reader: `.\src\Reader\run_Reader.ps1 -CheckDb Si` (диагностика без GUI).
- Проверка порта из PowerShell: `Test-NetConnection -ComputerName 127.0.0.1 -Port 5432`.

## Рекомендация

**Немедленно:** Запустить мониторинг для выявления источника stop-команд:
```powershell
# PowerShell - отслеживать перезапуски контейнера
while ($true) { 
    $status = wsl docker inspect artas_postgres --format '{{.State.StartedTime}}' 2>$null
    Write-Host "$(Get-Date): $status"
    Start-Sleep -Seconds 10 
}
```

**Долгосрочно:** Добавить retry-логику в провайдер соединений `NpgsqlDataSource`.
