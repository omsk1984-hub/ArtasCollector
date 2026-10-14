# Безопасная очистка Docker и WSL пространства
# Использование: .\cleanup_docker_wsl.ps1
# Опции:
#   .\cleanup_docker_wsl.ps1 -full    # Полная очистка (образы, build cache, неиспользуемые volumes)
#
# ВАЖНО про ограничения Docker:
#   • Образы, используемые ЗАПУЩЕННЫМИ контейнерами, удалить нельзя (docker вернёт conflict).
#     Их можно убрать только после остановки контейнеров.
#   • `docker volume prune` удаляет ТОЛЬКО volumes, не привязанные к контейнерам.
#     Volumes работающих сервисов (db_postgres_data, monitoring_*) сохраняются.

param(
    [switch]$Full
)

function Write-Section($msg) {
    Write-Host "`n=== $msg ===" -ForegroundColor Cyan
}

Write-Host "Очистка Docker и WSL пространства..." -ForegroundColor Cyan
if ($Full) {
    Write-Host "[Полная очистка]" -ForegroundColor Yellow
}

Write-Section "1. Статус Docker перед очисткой"
wsl bash -c "docker system df"

Write-Section "2. Очистка Docker мусора"
if ($Full) {
    Write-Host "Полная очистка (образы, build cache, volumes, сети)... БЕЗ удаления контейнеров" -ForegroundColor Yellow

    # --- 2.0. Удаление dangling образов (без тега <none>) ---
    wsl bash -c 'docker image prune -f'

    # --- 2.1. Удаление неиспользуемых образов ---
    # `docker image prune -a -f` удаляет ВСЕ образы, не привязанные ни к одному
    # контейнеру (ни running, ни stopped). Образы активных контейнеров сохраняются —
    # Docker сам их защищает (docker rmi -f их всё равно не удалит и вернёт conflict).
    # Это надёжнее ручного перебора `docker images -q`, где ID, разделённые
    # переносами строк, ломали команду (bash трактовал строки как команды).
    Write-Section "2.1. Удаление неиспользуемых Docker образов"
    $imgBefore = wsl bash -c "docker images -q | wc -l"
    wsl bash -c 'docker image prune -a -f'
    $imgAfter = wsl bash -c "docker images -q | wc -l"
    $imgRemoved = [int]$imgBefore - [int]$imgAfter
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Неиспользуемые образы удалены: было $imgBefore, осталось $imgAfter (удалено $imgRemoved)" -ForegroundColor Yellow
    } else {
        Write-Host "Ошибка удаления образов (код: $LASTEXITCODE)" -ForegroundColor Red
    }
    if ($imgAfter -gt 0) {
        Write-Host "ПРИМЕЧАНИЕ: оставшиеся $imgAfter образов используются контейнерами — сначала остановите их, чтобы удалить образы." -ForegroundColor DarkGray
    }

    # --- 2.2. Очистка build cache ---
    # docker image prune НЕ трогает build cache — нужен отдельный docker builder prune.
    Write-Section "2.2. Очистка Docker build cache"
    wsl bash -c 'docker builder prune -a -f'
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Build cache очищен" -ForegroundColor Yellow
    } else {
        Write-Host "Ошибка очистки build cache (код: $LASTEXITCODE)" -ForegroundColor Red
    }

    # --- 2.3. Удаление неиспользуемых сетей ---
    Write-Section "2.3. Удаление неиспользуемых сетей"
    wsl bash -c 'docker network prune -f'

    # --- 2.4. Удаление неиспользуемых volumes (ОСТОРОЖНО - потеря данных!) ---
    # `docker volume prune` удаляет ТОЛЬКО volumes, не привязанные к контейнерам.
    # Volumes работающих сервисов (db_postgres_data, monitoring_*) останутся на месте.
    Write-Section "2.4. Удаление неиспользуемых volumes"
    $CONFIRM_VOLS = Read-Host "Удалить НЕИСПОЛЬЗУЕМЫЕ volumes? Данные будут потеряны! (y/N)"
    if ($CONFIRM_VOLS -eq 'y' -or $CONFIRM_VOLS -eq 'Y') {
        wsl bash -c 'docker volume prune -f'
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Неиспользуемые volumes удалены (используемые контейнерами сохранились)" -ForegroundColor Yellow
        } else {
            Write-Host "Ошибка удаления volumes (код: $LASTEXITCODE)" -ForegroundColor Red
        }
    } else {
        Write-Host "Volumes не тронуты" -ForegroundColor Gray
    }

    Write-Host "`nВАЖНО: Контейнеры сохранены!" -ForegroundColor Green
    Write-Host "Чтобы восстановить удалённые образы: .\save_docker_images.ps1 / .\load_docker_images.ps1" -ForegroundColor Gray
} else {
    Write-Host "Безопасная очистка (только кеши)..." -ForegroundColor Gray
    # Удаляем только остановленные контейнеры и их логи
    wsl bash -c 'docker container prune -f' 2>$null
    # В безопасном режиме тоже чистим dangling-образы и build cache
    wsl bash -c 'docker image prune -f' 2>$null
    wsl bash -c 'docker builder prune -f' 2>$null
}

Write-Section "3. Очистка кэша apt в WSL"
wsl bash -c 'sudo apt clean && sudo apt autoremove -y' 2>$null
Write-Host "Кэш apt очищен" -ForegroundColor Green

Write-Section "4. Очистка временных файлов WSL"
wsl bash -c "find /tmp -mindepth 1 -not -path '/tmp/snap-*' -not -path '/tmp/systemd-*' -exec rm -rf {} + 2>/dev/null; true"
Write-Host "Временные файлы WSL очищены" -ForegroundColor Green

Write-Section "5. Чистка Loki и Promtail от старых логов"

# Контейнер Loki имеет фиксированное имя из docker-compose.monitoring.yml
$LOKI_CONTAINER = "artas_loki"
$PROMTAIL_CONTAINER = "artas_promtail"
$COMPOSE_DIR = "/mnt/d/work/Artas2026/monitoring"
$COMPOSE_FILE = "docker-compose.monitoring.yml"

# Проверяем запущен ли контейнер Loki
$LOKI_RUNNING = wsl bash -c "docker ps --filter 'name=$LOKI_CONTAINER' --format '{{.Names}}' 2>/dev/null"
if ($LOKI_RUNNING) {
    Write-Host "Loki контейнер найден: $LOKI_CONTAINER" -ForegroundColor Green
    
    # Показать размер данных Loki до очистки (в образе Loki нет утилиты du,
    # поэтому смотрим размер volume через docker system df)
    wsl bash -c "docker system df -v | grep -i loki || echo 'size check failed'"
    
    # Опционально: очистить данные Loki (ТОЛЬКО если Full mode)
    if ($Full) {
        Write-Host "`nВНИМАНИЕ: Очистка данных Loki удалит ВСЕ логи!" -ForegroundColor Red
        Write-Host "Контейнер будет остановлен, volume удалён, контейнер перезапущен." -ForegroundColor Yellow
        $CONFIRM = Read-Host "Удалить все логи Loki? (y/N)"
        if ($CONFIRM -eq 'y' -or $CONFIRM -eq 'Y') {
            # Шаг 1: Остановить и удалить контейнер Loki через docker compose
            Write-Host "1. Останавливаю и удаляю контейнер Loki через docker compose..." -ForegroundColor Gray
            wsl bash -c "cd $COMPOSE_DIR && docker compose -f $COMPOSE_FILE stop loki && docker compose -f $COMPOSE_FILE rm -f loki"
            
            # Шаг 2: Удалить volume с данными Loki (именно это реально очищает данные)
            Write-Host "2. Удаляю volume Loki..." -ForegroundColor Gray
            # Docker Compose создаёт volume с префиксом имени папки: monitoring_loki_data
            wsl bash -c "docker volume ls --format '{{.Name}}' | grep loki | xargs -r docker volume rm -f 2>/dev/null || echo 'Volume Loki не найден (возможно уже удалён)'"
            
            # Шаг 3: Пересоздать контейнер через docker compose
            Write-Host "3. Пересоздаю контейнер Loki через docker compose..." -ForegroundColor Gray
            wsl bash -c "cd $COMPOSE_DIR && docker compose -f $COMPOSE_FILE up -d loki"

            # Шаг 4: Пересоздать Promtail, чтобы сбросить /tmp/positions.yaml.
            # Иначе Promtail после пересоздания пустого Loki заново перешлёт всё
            # содержимое уже прочитанных лог-файлов (логи вернутся).
            # Останавливаем Promtail ДО пересоздания Loki не нужно — порядок не важен,
            # важно пересоздать Promtail после рестарта Loki.
            Write-Host "4. Пересоздаю контейнер Promtail (сброс позиций)..." -ForegroundColor Gray
            # --remove-orphans удаляет ручные/осиротевшие контейнеры promtail
            # (например, запущенные через docker run без container_name).
            wsl bash -c "cd $COMPOSE_DIR && docker compose -f $COMPOSE_FILE rm -f promtail ; docker compose -f $COMPOSE_FILE up -d promtail --remove-orphans"
            
            if ($LASTEXITCODE -eq 0) {
                Write-Host "`nЛоки и Promtail полностью очищены и перезапущены!" -ForegroundColor Yellow
                Write-Host "Проверьте: http://localhost:3100/loki/api/v1/query_range?query={app=%22artas2026%22}" -ForegroundColor Gray
            } else {
                Write-Host "ОШИБКА при перезапуске Loki/Promtail (код: $LASTEXITCODE)" -ForegroundColor Red
                Write-Host "Запустите вручную:" -ForegroundColor Gray
                Write-Host "  cd monitoring && docker compose -f docker-compose.monitoring.yml up -d loki promtail" -ForegroundColor Gray
            }
        }
    }
} else {
    Write-Host "Loki контейнер не найден, пробуем пересоздать с нуля..." -ForegroundColor Yellow
    if ($Full) {
        Write-Host "Удаляю volume Loki (если есть)..." -ForegroundColor Gray
        wsl bash -c "docker volume ls --format '{{.Name}}' | grep loki | xargs -r docker volume rm -f 2>/dev/null || true"
    }
    wsl bash -c "cd $COMPOSE_DIR && docker compose -f $COMPOSE_FILE up -d loki"
    Write-Host "Контейнер Loki запущен" -ForegroundColor Green
}

Write-Host "`nСовет: retention_period: 48h (2 дня) настроен в config.yml и подключён в docker-compose — старые логи удаляются автоматически (compactor)." -ForegroundColor Gray

Write-Section "6. Итоговый статус Docker"
wsl bash -c "docker system df"

Write-Section "7. Размер баз данных в WSL"
# Размер данных Docker в WSL
$DOCKER_SIZE = wsl bash -c "du -sh /var/lib/docker 2>/dev/null | cut -f1"
if ($DOCKER_SIZE) {
    Write-Host "Docker: $DOCKER_SIZE" -ForegroundColor White
}

# Размер базы PostgreSQL в Docker
$PG_SIZE = wsl bash -c "du -sh /var/lib/postgresql/data 2>/dev/null | cut -f1"
if ($PG_SIZE) {
    Write-Host "PostgreSQL: $PG_SIZE" -ForegroundColor White
}

# Размер данных Loki (WSL путь)
$LOKI_SIZE = wsl bash -c "du -sh /home/$env:USERNAME/Artas2026/monitoring/loki 2>/dev/null | cut -f1"
if ($LOKI_SIZE) {
    Write-Host "Loki: $LOKI_SIZE" -ForegroundColor White
}

# Проверить размер дисков WSL
Write-Host "`nРазмер VHDX файлов:" -ForegroundColor White
Get-ChildItem "$env:LOCALAPPDATA\Docker\wsl" -Filter *.vhdx |
    Sort-Object Length -Descending |
    Format-Table Name, @{Label="Размер(GB)"; Expression={[math]::Round($_.Length/1GB, 2)}} -AutoSize

Write-Host "`nГотово. Очистка выполнена — сравните раздел '1. Статус Docker перед очисткой' и '6. Итоговый статус Docker' выше." -ForegroundColor Green
Write-Host "ВАЖНО: освобождённое место остаётся ВНУТРИ WSL — файл ext4.vhdx на диске Windows сам не сжимается." -ForegroundColor Yellow
Write-Host "Чтобы вернуть место на физический диск, выполните (при остановленных сервисах):" -ForegroundColor Gray
Write-Host "  wsl --shutdown" -ForegroundColor Gray
Write-Host "  wsl --manage <distro> --set-sparse true      # либо Optimize-VHD -Path <путь> -Mode Full" -ForegroundColor Gray
