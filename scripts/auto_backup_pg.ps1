# Автоматическое резервное копирование PostgreSQL
# Создаёт дамп базы данных в папку db/backups/
# Использование:
#   .\scripts\auto_backup_pg.ps1              #backup один раз
#   .\scripts\auto_backup_pg.ps1 -Register    #добавить в запланированные задачи ежедневно в 22:00

param(
    [switch]$Register,
    [switch]$Unregister
)

$TaskName = "Artas_PgBackup"
$ScriptPath = "$((Get-Location).Path)\scripts\auto_backup_pg.ps1"
$BackupDir = "$((Get-Location).Path)\db\backups"
$MaxBackups = 10 # Максимум бэкапов для хранения

if ($Register) {
    Write-Host "Регистрация задачи автобэкапа..." -ForegroundColor Cyan
    
    $existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($existing) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-Host "Старая задача удалена." -ForegroundColor Yellow
    }
    
    # Создаём директорию для бэкапов
    if (-not (Test-Path $BackupDir)) {
        New-Item -ItemType Directory -Path $BackupDir | Out-Null
    }
    
    $action = New-ScheduledTaskAction -Execute "powershell.exe" `
        -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$ScriptPath`""
    
    $trigger = New-ScheduledTaskTrigger -Daily -At "22:00"
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
    
    try {
        Register-ScheduledTask `
            -TaskName $TaskName `
            -Action $action `
            -Trigger $trigger `
            -Settings $settings `
            -Description "Автоматическое резервное копирование PostgreSQL ежедневно в 22:00" `
            -User (whoami) `
            -ErrorAction Stop
        
        Write-Host "`nЗадача '$TaskName' создана! Бэкап будет делаться ежедневно в 22:00." -ForegroundColor Green
    } catch {
        Write-Host "[ОШИБКА] Не удалось зарегистрировать задачу '$TaskName': $_" -ForegroundColor Red
    }
}
elseif ($Unregister) {
    Write-Host "Удаление задачи автобэкапа..." -ForegroundColor Cyan
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    Write-Host "Задача '$TaskName' удалена." -ForegroundColor Yellow
}
else {
    # Выполнение бэкапа PostgreSQL из Docker-контейнера
    Write-Host "Выполняется бэкап PostgreSQL (Docker)..." -ForegroundColor Cyan
    
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $backupFile = "$BackupDir\postgres_backup_$timestamp.dump"
    $containerName = "artas_postgres"
    $pgUser = "artas_user"
    $pgPassword = "artas_password"
    $pgDb = "artas_db"
    
    try {
        Write-Host "Контейнер: $containerName" -ForegroundColor Gray
        
        # Запускаем pg_dump внутри Docker-контейнера с сохранением в файл
        # Переменные PowerShell раскрываются ДО передачи в WSL bash
        $dumpCmd = "sudo docker exec ${containerName} pg_dump -U ${pgUser} -d ${pgDb} --format=custom -f /tmp/pg_backup_${timestamp}.dump"
        wsl bash -c "${dumpCmd}" 2>&1 | Out-Null
        
        if ($LASTEXITCode -eq 0) {
            # Копируем файл из контейнера в хост-систему
            $cpCmd = "sudo docker cp ${containerName}:/tmp/pg_backup_${timestamp}.dump /mnt/d/work/Artas2026/db/backups/postgres_backup_${timestamp}.dump && sudo rm /tmp/pg_backup_${timestamp}.dump"
            wsl bash -c "${cpCmd}" 2>&1 | Out-Null
        }
        
        Start-Sleep -Seconds 2
        
        if (Test-Path $backupFile) {
            $sizeMB = [math]::Round((Get-Item $backupFile).Length / 1MB, 2)
            
            if ($sizeMB -eq 0) {
                Write-Host "Бэкап пустой ($sizeMB МБ)! Проверьте PostgreSQL." -ForegroundColor Red
            } else {
                Write-Host "Бэкап создан: $backupFile ($sizeMB МБ)" -ForegroundColor Green
                
                # Удаляем старые бэкапы если больше MaxBackups
                $oldBackups = Get-ChildItem -Path $BackupDir -Filter "postgres_backup_*.dump" | Sort-Object Name | Select-Object -First ([Math]::Max(0, (Get-ChildItem -Path $BackupDir -Filter "postgres_backup_*.dump").Count - $MaxBackups))
                
                if ($oldBackups) {
                    $oldBackups | Remove-Item -Force
                    Write-Host "Удалены старые бэкапы." -ForegroundColor Gray
                }
            }
        } else {
            Write-Host "Бэкап не был создан. Проверьте PostgreSQL." -ForegroundColor Red
        }
    } catch {
        Write-Host "[ОШИБКА] Бэкап не выполнен: $_" -ForegroundColor Red
    }
}
