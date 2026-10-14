# Развёртывание Loki в WSL для Artas2026

## Обзор

Стек мониторинга на основе **Grafana Loki** для сбора и хранения логов проекта Artas2026. Просмотр логов — через Loki API (http://localhost:3100).

**Важно:** Docker запущен внутри **WSL (Windows Subsystem for Linux)**, поэтому управление осуществляется через WSL терминал.

## Требования

- **WSL2**: Ubuntu на Windows
- **Docker**: Docker Desktop с WSL2 backend или Docker CE в WSL
- **RAM**: Минимум 4GB для Docker
- **Диск**: Минимум 10GB для хранения логов

## Как определить, что Docker в WSL работает

```bash
# Запустите WSL
wsl

# Проверьте Docker внутри WSL
docker ps

# Если Docker не установлен в WSL:
sudo apt-get update
sudo apt-get install docker.io
sudo systemctl enable --now docker

# Или установите через Docker Desktop (автоматически)
```

## Быстрый старт в WSL

### Шаг 1: Откройте терминал WSL

```powershell
# Из PowerShell (Windows)
wsl
```

или откройте **Ubuntu** из меню Пуск.

### Шаг 2: Перейдите в проект

```bash
cd /mnt/d/work/Artas2026
```

### Шаг 3: Проверьте Docker

```bash
docker ps
```

### Шаг 4: Сделайте скрипт исполняемым

```bash
chmod +x scripts/manage_monitoring.sh
```

### Шаг 5: Запустите стек мониторинга

```bash
./scripts/manage_monitoring.sh start
```

Или напрямую через Docker Compose:

```bash
cd monitoring
docker compose -f docker-compose.monitoring.yml up -d
```

### Шаг 6: Проверьте статус

```bash
./scripts/manage_monitoring.sh status
```

Или проверьте вручную:

```bash
curl http://localhost:3100/-/healthy
```

### Шаг 7: Проверьте логи через Loki API

```bash
# Список меток
curl http://localhost:3100/loki/api/v1/labels

# Логи Writer за последнее время
curl "http://localhost:3100/loki/api/v1/query_range?query={job=%22artas_writer%22}&limit=10"
```

## Управление сервисами (только из WSL)

### Через bash скрипт

```bash
# Перейдите в проект
cd /mnt/d/work/Artas2026

# Запуск
./scripts/manage_monitoring.sh start

# Остановка
./scripts/manage_monitoring.sh stop

# Перезапуск
./scripts/manage_monitoring.sh restart

# Статус
./scripts/manage_monitoring.sh status

# Логи всех сервисов
./scripts/manage_monitoring.sh logs

# Логи конкретного сервиса
./scripts/manage_monitoring.sh logs loki
./scripts/manage_monitoring.sh logs promtail

# Полная очистка (удаление данных!)
./scripts/manage_monitoring.sh cleanup

# Справка
./scripts/manage_monitoring.sh help
```

### Прямые команды Docker Compose (из WSL)

```bash
cd /mnt/d/work/Artas2026/monitoring

# Запуск
docker compose -f docker-compose.monitoring.yml up -d

# Остановка
docker compose -f docker-compose.monitoring.yml down

# Перезапуск
docker compose -f docker-compose.monitoring.yml restart

# Просмотр статуса
docker compose -f docker-compose.monitoring.yml ps

# Просмотр логов
docker compose -f docker-compose.monitoring.yml logs -f
docker compose -f docker-compose.monitoring.yml logs -f loki

# Обновление образов
docker compose -f docker-compose.monitoring.yml pull
docker compose -f docker-compose.monitoring.yml up -d

# Полная очистка
docker compose -f docker-compose.monitoring.yml down -v
```

## Почему PowerShell скрипт не работает

PowerShell выполняется в Windows, но Docker запущен внутри WSL. Поэтому PowerShell скрипт [`scripts/manage_monitoring.ps1`](scripts/manage_monitoring.ps1:1) попытается вызвать WSL из Windows, но это может быть ненадёжно.

**Рекомендация:** используйте bash скрипт `manage_monitoring.sh` напрямую из WSL терминала.

## Архитектура

```
┌──────────────────────────────────────────────────────┐
│                  WSL2 (Ubuntu)                        │
│                                                       │
│  ┌─────────────┐     ┌──────────────┐                │
│  │ Artas2026   │────>│   Loki       │                │
│  │   (C#)      │ HTTP│ :3100        │                │
│  └─────────────┘     └──────────────┘                │
│                            ↑                          │
│                    ┌───────┴───────┐                  │
│                    │   Promtail   │                  │
│                    │ (сбор логов) │                  │
│                    └──────────────┘                  │
└──────────────────────────────────────────────────────┘
           ↓ порты проброшены в Windows
┌──────────────────────────────────────────────────────┐
│              Windows Browser / curl                   │
│                                                       │
│  http://localhost:3100 → Loki API                     │
└──────────────────────────────────────────────────────┘
```

## Настройка логирования

Сервисы Artas2026 (C#) пишут структурированные логи через `ILogger<T>` в файлы
(например, `src/Writer/logs/writer.*.stdout.log`, `src/Stat/logs/`). Promtail собирает
эти файлы и отправляет их в Loki.

### Конфигурация через .env

```bash
# Каталог логов сервисов (монтируется в Promtail как /var/log/artas)
LOGS_DIR=D:\work\Artas2026\logs
```

Пути сбора логов задаются в [`monitoring/promtail/config.yml`](../monitoring/promtail/config.yml).

## LogQL запросы

### Базовые

```logql
# Все логи приложения
{job=~"artas_writer|artas_stat"}

# Логи конкретного сервиса
{job="artas_writer"}
{job="artas_stat"}

# Только ошибки
{job=~"artas_writer|artas_stat"} |= "ERROR"
```

### Продвинутые

```logql
# Подсчёт логов по уровням за 5 минут
count by (level) (sum count_over_time({job=~"artas_writer|artas_stat"}[5m]))

# Топ источников с ошибками
topk(10, sum by (job) (count({job=~"artas_writer|artas_stat"} |= "ERROR") > 0))
```

## Автозапуск при старте WSL

Добавьте в `~/.bashrc`:

```bash
# Auto-start Artas2026 monitoring stack on WSL login
if pgrep -f artas_loki > /dev/null 2>&1; then
    echo "Monitoring stack is already running"
else
    echo "Starting Artas2026 monitoring stack..."
    cd /mnt/d/work/Artas2026
    ./scripts/manage_monitoring.sh start > /dev/null 2>&1 &
fi
```

## Решение проблем

### Docker не найден в WSL

```bash
# Проверка
docker ps

# Установка Docker в WSL
sudo apt-get update
sudo apt-get install docker.io
sudo systemctl start docker
sudo systemctl enable docker

# Или используйте Docker Desktop с WSL integration
```

### Порты уже заняты

```bash
# Проверить что использует порт
sudo lsof -i :3100

# Изменить порты в monitoring/docker-compose.monitoring.yml
```

### Не удаётся получить доступ к Loki из Windows

Убедитесь, что Docker пробрасывает порты:

```bash
# Проверка из WSL
curl http://localhost:3100/-/healthy

# Проверка из Windows PowerShell
Invoke-WebRequest -Uri http://localhost:3100/-/healthy
```

### Логи не появляются в Loki

1. Проверьте что приложение отправляет логи в Loki
2. Проверьте Promtail логи:
   ```bash
   docker compose -f docker-compose.monitoring.yml logs promtail
   ```
3. Убедитесь что файлы логов существуют:
   ```bash
   ls -la /var/log/artas-writer/ /var/log/artas-stat/
   ```

## Полный список файлов

```
Artas2026/
├── monitoring/                    # Стек мониторинга
│   ├── docker-compose.monitoring.yml
│   ├── README.md
│   ├── loki/config.yml
│   ├── promtail/config.yml
│   ├── prometheus/prometheus.yml
│   └── otel-collector/config.yml
├── scripts/
│   ├── manage_monitoring.sh       # ← Используйте этот (WSL)
│   └── manage_monitoring.ps1      # PowerShell (не рекомендуется)
├── src/Writer/                    # C# Writer (источник логов)
├── src/Stat/                      # C# Stat (источник логов)
├── plans/                         # Планы (markdown)
└── .env.example
```
