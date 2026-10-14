# Восстановление PostgreSQL после повреждения WAL-файлов
# Последовательность действий (от мягкого к жёсткому):
#   0. Если контейнер отсутствует — поднять через docker compose up -d postgres
#   1. Диагностика реального состояния (health, recovery, признаки corruption в логах)
#   2. Graceful restart БЕЗ удаления контейнера (docker compose restart)
#   3. Проверка резервных копий
#   4. Полный сброс данных — ТОЛЬКО с явным -ForceReset и подтверждением
#
# Заменяет ставший избыточным fix_pg_after_shutdown.ps1 (объединён в этот скрипт).
#
# ВАЖНО: скрипт НЕ удаляет данные PostgreSQL без явного флага -ForceReset,
# подтверждённого вводом слова DELETE. Данные живут в volume db_postgres_data
# (имя формируется Compose: имя каталога проекта "db" + volume "postgres_data").
#
# Использование:
#   .\scripts\fix_wal_corruption.ps1              # только диагностика + мягкий restart
#   .\scripts\fix_wal_corruption.ps1 -DryRun      # показать план без выполнения
#   .\scripts\fix_wal_corruption.ps1 -ForceReset  # разрешить сброс данных (с подтверждением)

param(
    [switch]$ForceReset, # разрешить переход к сбросу данных (всё равно спросит подтверждение)
    [switch]$DryRun      # показать план без выполнения
)

$PSScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$dbDir = "D:\work\Artas2026\db"
$composeFile = "$dbDir\docker-compose.yml"
$backupDir = "$dbDir\backups"
$volumeName = "db_postgres_data"

Write-Host "`n=========================================" -ForegroundColor Cyan
Write-Host "Восстановление PostgreSQL после WAL corruption" -ForegroundColor Cyan
Write-Host "=========================================`n" -ForegroundColor Cyan

if ($DryRun) {
    Write-Host "[DryRun] План (без выполнения):" -ForegroundColor Magenta
    Write-Host "  1. Диагностика (логи, health, pg_is_in_recovery)"
    Write-Host "  2. docker compose restart postgres (без удаления контейнера и volume)"
    Write-Host "  3. Проверка бэкапов"
    Write-Host "  4. Сброс данных — только с -ForceReset и подтверждением"
    exit 0
}

# Возвращает статус health из Docker: healthy / unhealthy / starting / none / unknown
function Get-PgHealth {
    $h = wsl docker inspect artas_postgres --format "{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}" 2>$null
    if ($h) { return $h.Trim() }
    return "unknown"
}

# Если контейнера нет (удалён/не создан) — поднимаем его через compose up.
# Это объединённая логика из fix_pg_after_shutdown.ps1.
$containerStatus = wsl docker ps -a --filter name=artas_postgres --format "{{.Status}}" 2>$null
if (-not $containerStatus) {
    Write-Host "Контейнер artas_postgres отсутствует — поднимаю через docker compose up -d postgres..." -ForegroundColor Yellow
    wsl bash -c "cd /mnt/d/work/Artas2026/db && docker compose up -d postgres" 2>$null
    Start-Sleep -Seconds 15

    $statusAfterUp = wsl docker ps --filter name=artas_postgres --format "{{.Status}}" 2>$null
    if (-not $statusAfterUp) {
        Write-Host "Не удалось поднять PostgreSQL." -ForegroundColor Red
        exit 1
    }
    Write-Host "PostgreSQL поднят: $statusAfterUp" -ForegroundColor Green
}

# === ШАГ 1: Диагностика ===
Write-Host "[1/4] Диагностика..." -ForegroundColor Yellow
$logOutput = (wsl docker logs --tail 80 artas_postgres 2>&1) -join "`n"

$health = Get-PgHealth
Write-Host "Health контейнера: $health" -ForegroundColor $(if($health -eq 'healthy'){'Green'}else{'Yellow'})

# PG 16 при чистом старте пишет "database system is ready to accept connections".
if ($logOutput -match "ready to accept connections" -and $health -eq "healthy") {
    Write-Host "PostgreSQL работает и принимает подключения — вмешательство не требуется." -ForegroundColor Green
    exit 0
}

$corruptionDetected = $false
if ($logOutput -match "corrupted page|invalid record|bad checksum|invalid page|could not read block") {
    Write-Host "Обнаружены признаки повреждения данных (corruption)!" -ForegroundColor Red
    $corruptionDetected = $true
} elseif ($logOutput -match "recovery starting|redo at|automatic recovery in progress") {
    Write-Host "PostgreSQL в процессе восстановления (recovery) — ожидание..." -ForegroundColor Yellow
    Start-Sleep -Seconds 30
    $healthAfter = Get-PgHealth
    if ($healthAfter -eq "healthy") {
        Write-Host "Recovery завершился успешно, база доступна." -ForegroundColor Green
        exit 0
    }
} elseif ($logOutput -match "unexpected shutdown|fast shutdown") {
    Write-Host "Обнаружено нештатное завершение — вероятен recover cycle при внешнем stop." -ForegroundColor Yellow
}

# Проверим recovery через SQL (если порт открыт)
$sqlRecovery = wsl docker exec artas_postgres psql -U artas_user -d artas_db -tAc "SELECT pg_is_in_recovery();" 2>$null
if ($sqlRecovery) { Write-Host "pg_is_in_recovery(): $($sqlRecovery.Trim())" -ForegroundColor Gray }

if (-not $corruptionDetected) {
    Write-Host "Признаков повреждения WAL не найдено — вероятна внешняя остановка, а не corruption." -ForegroundColor Green
}

# Если база уже здорова и не в recovery/corruption — мягкий restart не нужен.
if ($health -eq "healthy" -and -not $corruptionDetected) {
    Write-Host "База здорова — мягкий restart не требуется." -ForegroundColor Green
    exit 0
}

# === ШАГ 2: Мягкий restart (без удаления контейнера и volume) ===
Write-Host "`n[2/4] Мягкий restart (docker compose restart, данные сохраняются)..." -ForegroundColor Yellow

wsl bash -c "cd /mnt/d/work/Artas2026/db && docker compose restart postgres" 2>$null

Start-Sleep -Seconds 20
$status = wsl docker ps --filter name=artas_postgres --format "{{.Status}}" 2>$null
$health = Get-PgHealth

if ($health -eq "healthy") {
    Write-Host "PostgreSQL восстановлен после мягкого restart!" -ForegroundColor Green
    exit 0
}

Write-Host "Мягкий restart не помог. Текущий статус: $status" -ForegroundColor Red

# === ШАГ 3: Проверка резервных копий ===
Write-Host "`n[3/4] Проверка резервных копий..." -ForegroundColor Yellow

$latestBackup = $null
if (Test-Path $backupDir) {
    $latestBackup = Get-ChildItem -Path $backupDir -Filter "postgres_backup_*.dump" | Sort-Object Name -Descending | Select-Object -First 1
}

if ($latestBackup) {
    $sizeMB = [math]::Round($latestBackup.Length / 1MB, 2)
    Write-Host "Найден бэкап: $($latestBackup.Name) ($sizeMB МБ)" -ForegroundColor Green
} else {
    Write-Host "Бэкапов не найдено!" -ForegroundColor Red
}

# === ШАГ 4: Сброс данных — ТОЛЬКО с явным -ForceReset и подтверждением ===
Write-Host "`n[4/4] Сброс данных..." -ForegroundColor Yellow

if (-not $ForceReset) {
    Write-Host "Сброс данных НЕ выполняется: не указан флаг -ForceReset." -ForegroundColor Green
    Write-Host "Данные PostgreSQL сохранены (volume '$volumeName' нетронут)." -ForegroundColor Green
    Write-Host ""
    Write-Host "Если действительно нужно удалить все данные и пересоздать базу:" -ForegroundColor Yellow
    Write-Host "  1. Сначала сделайте бэкап:   .\scripts\auto_backup_pg.ps1" -ForegroundColor White
    Write-Host "  2. Затем запустите:          .\scripts\fix_wal_corruption.ps1 -ForceReset" -ForegroundColor White
    Write-Host ""
    Write-Host "================================================================`n" -ForegroundColor Cyan
    exit 1
}

Write-Host "ВНИМАНИЕ: это УДАЛИТ все текущие данные PostgreSQL (volume '$volumeName')!" -ForegroundColor Red
Write-Host "Порт 5432, текущий объём данных — фракталы, тики и т.д. будут потеряны." -ForegroundColor Red
$confirm = Read-Host "Для подтверждения введите слово DELETE"
if ($confirm -ne "DELETE") {
    Write-Host "Отменено пользователем. Данные сохранены." -ForegroundColor Green
    exit 1
}

if (-not $latestBackup) {
    Write-Host "Свежий бэкап отсутствует. Продолжение может привести к безвозвратной потере данных." -ForegroundColor Red
    $confirm2 = Read-Host "Продолжить БЕЗ бэкапа? Введите YES"
    if ($confirm2 -ne "YES") {
        Write-Host "Отменено пользователем. Данные сохранены." -ForegroundColor Green
        exit 1
    }
}

Write-Host "Останавливаем контейнер и удаляем volume..." -ForegroundColor Yellow
wsl docker stop artas_postgres 2>$null
wsl docker rm artas_postgres 2>$null
wsl docker volume rm $volumeName 2>$null

wsl bash -c "cd /mnt/d/work/Artas2026/db && docker compose up -d postgres" 2>$null

Start-Sleep -Seconds 30

# === Проверка результата ===
Write-Host "`nПроверка результата..." -ForegroundColor Yellow

$finalStatus = wsl docker ps --filter name=artas_postgres --format "{{.Names}} {{.Status}}" 2>$null
Write-Host "Статус: $finalStatus" -ForegroundColor $(if($finalStatus -match 'healthy'){'Green'}else{'Red'})

$portOpen = (Test-NetConnection -ComputerName '127.0.0.1' -Port 5432 -WarningAction SilentlyContinue).TcpTestSucceeded
$portResult = if ($portOpen) { 'OPEN' } else { 'CLOSED' }
Write-Host "Порт localhost:5432: $portResult" -ForegroundColor $(if($portOpen){'Green'}else{'Red'})

if ($portResult -eq 'OPEN') {
    Write-Host "`n✅ База пересоздана (пустая)." -ForegroundColor Green
    if ($latestBackup) {
        Write-Host "Восстановите данные из бэкапа:" -ForegroundColor Yellow
        Write-Host "  wsl docker exec -i artas_postgres pg_restore -U artas_user -d artas_db < db/backups/$($latestBackup.Name)" -ForegroundColor White
    } else {
        Write-Host "Бэкапов нет — примените миграции для создания структуры:" -ForegroundColor Yellow
        Write-Host "  wsl bash -c 'cd /mnt/d/work/Artas2026/db && docker compose up -d migrator'" -ForegroundColor White
    }
    Write-Host "`n================================================================`n" -ForegroundColor Green
} else {
    Write-Host "`n❌ Пересоздание не удалось. Проверьте логи:" -ForegroundColor Red
    Write-Host "  wsl docker logs --tail 50 artas_postgres" -ForegroundColor White
    Write-Host "================================================================`n" -ForegroundColor Red
}
