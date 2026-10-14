# Мониторинг состояния PostgreSQL при старте системы
# Автоматически проверяет и чинит контейнер если он в цикле рестартов
# Использование:
#   .\scripts\monitor_pg_startup.ps1           # Проверить один раз + начать непрерывный мониторинг
#   .\scripts\monitor_pg_startup.ps1 -Register # Добавить в автозагрузку Windows
#   .\scripts\monitor_pg_startup.ps1 -Unregister # Убрать из автозагрузки

param(
    [switch]$Register,
    [switch]$Unregister,
    [switch]$OneShot
)

$TaskName = "Artas_PgMonitor"
$ScriptPath = "$((Get-Location).Path)\scripts\monitor_pg_startup.ps1"

function Test-PgContainer {
    param([int]$MaxRetries = 5)

    $containerStatus = wsl docker ps --filter name=artas_postgres --format "{{.Status}}" 2>$null

    if (-not $containerStatus) {
        return @{ Status = "NOT_RUNNING"; UptimeSeconds = 0 }
    }

    # Парсим uptime
    $uptimeMatch = $containerStatus -match 'Up (\d+) (seconds?|minutes?|hours?)'
    if ($uptimeMatch) {
        return @{
            Status = "RUNNING"
            RawStatus = $containerStatus
            UptimeSeconds = if ($Matches[2] -match "min") { [int]$Matches[1] * 60 }
                           elseif ($Matches[2] -match "hour") { [int]$Matches[1] * 3600 }
                           else { [int]$Matches[1] }
        }
    }

    return @{ Status = "UNKNOWN"; RawStatus = $containerStatus; UptimeSeconds = 0 }
}

# Возвращает реальное число перезапусков контейнера (RestartCount из Docker).
# ВАЖНО: короткий uptime сам по себе НЕ означает цикл рестартов —
# контейнер может быть только что запущен вручную. Признак проблемы —
# растущий RestartCount при падениях, а не возраст контейнера.
function Get-PgRestartCount {
    $rc = wsl docker inspect artas_postgres --format "{{.RestartCount}}" 2>$null
    if ($rc -match '^\d+$') { return [int]$rc }
    return -1
}

# Возвращает статус health из Docker: healthy / unhealthy / starting / none / unknown
function Get-PgHealth {
    $h = wsl docker inspect artas_postgres --format "{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}" 2>$null
    if ($h) { return $h.Trim() }
    return "unknown"
}

function Fix-PgCycle {
    Write-Host "[FIX] Обнаружены реальные перезапуски контейнера! Пересоздаём..." -ForegroundColor Yellow

    # Останавливаем контейнер
    wsl docker stop artas_postgres 2>$null
    wsl docker rm artas_postgres 2>$null

    # Пересоздаём (данные в volume сохраняются — WAL в порядке)
    wsl bash -c "cd /mnt/d/work/Artas2026/db && docker compose up -d postgres" 2>$null

    Start-Sleep -Seconds 20

    $newStatus = wsl docker ps --filter name=artas_postgres --format "{{.Status}}" 2>$null
    Write-Host "[FIX] Новый статус: $newStatus" -ForegroundColor $(if($newStatus -match 'healthy'){'Green'}else{'Red'})
}

if ($Register) {
    Write-Host "Регистрация мониторинга PostgreSQL в автозагрузку..." -ForegroundColor Cyan
    
    $existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($existing) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-Host "Старая задача удалена." -ForegroundColor Yellow
    }
    
    $action = New-ScheduledTaskAction -Execute "powershell.exe" `
        -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$ScriptPath`""
    
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User (whoami)
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
    
    Register-ScheduledTask `
        -TaskName $TaskName `
        -Action $action `
        -Trigger $trigger `
        -Settings $settings `
        -Description "Мониторинг PostgreSQL: автоматическое восстановление при цикле рестартов" `
        -User (whoami)
    
    Write-Host "`nЗадача '$TaskName' создана!" -ForegroundColor Green
    Write-Host "Скрипт запустится при каждом входе в систему и будет проверять PostgreSQL каждые 30 секунд." -ForegroundColor Gray
}
elseif ($Unregister) {
    Write-Host "Удаление задачи мониторинга..." -ForegroundColor Cyan
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    Write-Host "Задача '$TaskName' удалена." -ForegroundColor Yellow
}
else {
    # Основной режим мониторинга
    Write-Host "[START] Запуск мониторинга PostgreSQL..." -ForegroundColor Cyan

    $checkCount = 0
    $prevRestartCount = Get-PgRestartCount

    while ($true) {
        Start-Sleep -Seconds 30

        $result = Test-PgContainer
        $checkCount++

        switch ($result.Status) {
            "RUNNING" {
                $health = Get-PgHealth
                $restartCount = Get-PgRestartCount

                # Реальный признак проблемы — растущий RestartCount (контейнер падает
                # и Docker его перезапускает). Короткий uptime сам по себе НЕ является
                # поводом для вмешательства: контейнер мог быть просто запущен только что.
                if ($prevRestartCount -ge 0 -and $restartCount -gt $prevRestartCount) {
                    Write-Host "[CRASH] RestartCount вырос: $prevRestartCount -> $restartCount. Пересоздаём контейнер..." -ForegroundColor Red
                    $prevRestartCount = $restartCount
                    Fix-PgCycle
                    $prevRestartCount = Get-PgRestartCount
                }
                elseif ($health -eq "unhealthy") {
                    Write-Host "[WARN] Health=unhealthy. Разовая проверка через 30 сек..." -ForegroundColor Yellow
                    Start-Sleep -Seconds 30
                    $afterHealth = Get-PgHealth
                    if ($afterHealth -eq "unhealthy") {
                        Fix-PgCycle
                        $prevRestartCount = Get-PgRestartCount
                    }
                }
                else {
                    $prevRestartCount = $restartCount
                    if ($checkCount % 4 -eq 0) { # каждые 2 минуты
                        Write-Host "[OK] PG здоров ($health): $($result.RawStatus)" -ForegroundColor Green
                    }
                }
            }
            "NOT_RUNNING" {
                Write-Host "[START] PG не запущен. Запускаю..." -ForegroundColor Yellow
                
                wsl bash -c "cd /mnt/d/work/Artas2026/db && docker compose up -d postgres" 2>$null
                
                Start-Sleep -Seconds 15
                
                $tryAgain = Test-PgContainer
                $prevRestartCount = Get-PgRestartCount
                Write-Host "[TRY] Статус после запуска: $($tryAgain.RawStatus)" -ForegroundColor $(if($tryAgain.Status -eq 'RUNNING'){'Green'}else{'Red'})
            }
            default {
                Write-Host "[??] Неизвестный статус: $($result.RawStatus)" -ForegroundColor Magenta
            }
        }
    }
}
