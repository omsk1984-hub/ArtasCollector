# Автозапуск PostgreSQL после перезагрузки Windows
# Запускает docker compose up -d postgres
# Использование: запустите один раз чтобы зарегистрировать задачу:
#   powershell -ExecutionPolicy Bypass -File .\scripts\auto_start_postgres.ps1 -Register
# Убрать из автозагрузки:
#   powershell -ExecutionPolicy Bypass -File .\scripts\auto_start_postgres.ps1 -Unregister

param(
    [switch]$Register,
    [switch]$Unregister
)

$TaskName = "Artas_PgAutoStart"
$ScriptPath = "$((Get-Location).Path)\scripts\auto_start_postgres.ps1"
$DbDir = "$((Get-Location).Path)\db"

if ($Register) {
    Write-Host "Регистрация задачи автозапуска PostgreSQL..." -ForegroundColor Cyan
    
    # Удаляем старую задачу если есть
    $existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($existing) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-Host "Старая задача удалена." -ForegroundColor Yellow
    }
    
    # Создаём задачу которая выполняется при входе пользователя
    $action = New-ScheduledTaskAction -Execute "powershell.exe" `
        -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$ScriptPath`""
    
    $trigger = New-ScheduledTaskTrigger -AtLogOn -CurrentUser
    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatterries `
        -StartWhenAvailable `
        -RunLevel Highest
    
    Register-ScheduledTask `
        -TaskName $TaskName `
        -Action $action `
        -Trigger $trigger `
        -Settings $settings `
        -Description "Автозапуск PostgreSQL Docker контейнера после входа в Windows" `
        -User (whoami)
    
    Write-Host "`nЗадача '$TaskName' создана!" -ForegroundColor Green
    Write-Host "PostgreSQL автоматически запустится при следующем входе в Windows." -ForegroundColor Gray
}
elseif ($Unregister) {
    Write-Host "Удаление задачи автозапуска..." -ForegroundColor Cyan
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($task) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-Host "Задача '$TaskName' удалена." -ForegroundColor Yellow
    } else {
        Write-Host "Задача '$TaskName' не найдена." -ForegroundColor Gray
    }
}
else {
    # Без параметров — просто запуск (вызывается из scheduled task)
    Write-Host "[Auto Start] Запуск PostgreSQL..." -ForegroundColor Cyan
    
    # Ждём пока WSL полностью стартует
    Start-Sleep -Seconds 10
    
    try {
        wsl bash -c "cd /mnt/d/work/Artas2026/db && docker compose up -d postgres" 2>$null
        Start-Sleep -Seconds 15
        
        # Проверяем что порт доступен
        $portOpen = $false
        for ($i = 0; $i -lt 10; $i++) {
            $status = wsl docker ps --filter name=artas_postgres --format "{{.Status}}" 2>$null
            if ($status -and $status -match "healthy") {
                $portOpen = $true
                break
            }
            Start-Sleep -Seconds 3
        }
        
        if ($portOpen) {
            Write-Host "[Auto Start] PostgreSQL запущен и health OK!" -ForegroundColor Green
        } else {
            Write-Host "[Auto Start] PostgreSQL запущен, но ещё не healthy. Подождите немного." -ForegroundColor Yellow
        }
    } catch {
        Write-Host "[Auto Start] Ошибка запуска PostgreSQL: $_" -ForegroundColor Red
    }
}
