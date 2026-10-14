# Скрипт для перезапуска PostgreSQL контейнера после изменений в структуре базы данных
# Запуск: .\update.ps1  или  powershell -ExecutionPolicy Bypass -File update.ps1

Write-Host "Перезапуск PostgreSQL контейнера..." -ForegroundColor Cyan

# Проверяем, существует ли docker-compose
if (-not (Get-Command docker-compose -ErrorAction SilentlyContinue)) {
    Write-Host "Ошибка: docker-compose не найден. Установите Docker Desktop." -ForegroundColor Red
    exit 1
}

# Проверяем, запущен ли контейнер
$containerName = "artas_postgres"
$containerStatus = docker ps -q -f "name=$containerName"

if ($containerStatus) {
    Write-Host "Контейнер $containerName запущен, перезапускаем..." -ForegroundColor Yellow
    
    # Перезапускаем контейнер PostgreSQL
    docker-compose -f db/docker-compose.yml restart postgres
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Контейнер успешно перезапущен." -ForegroundColor Green
        
        # Ждем готовности PostgreSQL
        Write-Host "Ожидание готовности PostgreSQL..." -ForegroundColor Cyan
        $maxAttempts = 30
        $attempt = 0
        $isReady = $false
        
        while ($attempt -lt $maxAttempts -and -not $isReady) {
            try {
                $result = docker exec $containerName pg_isready -U artas_user -d artas_db 2>$null
                if ($result -like "*accepting connections*") {
                    $isReady = $true
                    Write-Host "PostgreSQL готов к работе." -ForegroundColor Green
                    break
                }
            } catch {
                # Игнорируем ошибки
            }
            
            $attempt++
            Write-Host "Попытка $attempt из $maxAttempts..." -ForegroundColor Gray
            Start-Sleep -Seconds 2
        }
        
        if (-not $isReady) {
            Write-Host "Предупреждение: PostgreSQL не ответил в течение ожидания." -ForegroundColor Yellow
        }
    } else {
        Write-Host "Ошибка при перезапуске контейнера." -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "Контейнер $containerName не запущен. Запускаем..." -ForegroundColor Yellow
    docker-compose -f db/docker-compose.yml up -d postgres
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Контейнер успешно запущен." -ForegroundColor Green
    } else {
        Write-Host "Ошибка при запуске контейнера." -ForegroundColor Red
        exit 1
    }
}

Write-Host "`nПрименение EF Core миграций к БД..." -ForegroundColor Cyan
$applyScript = Join-Path $PSScriptRoot "apply-migrations.ps1"
if (Test-Path $applyScript) {
    & $applyScript
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Ошибка применения миграций." -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "Скрипт apply-migrations.ps1 не найден, пропускаю применение миграций." -ForegroundColor Yellow
}

Write-Host "`nДополнительные команды для проверки:" -ForegroundColor Cyan
Write-Host "1. Проверить логи: docker-compose -f db/docker-compose.yml logs postgres" -ForegroundColor Gray
Write-Host "2. Подключиться к БД: docker exec -it artas_postgres psql -U artas_user -d artas_db" -ForegroundColor Gray
Write-Host "3. Проверить структуру таблицы fractals: \d fractals" -ForegroundColor Gray
Write-Host "4. Список миграций: .\apply-migrations.ps1 -List" -ForegroundColor Gray

Write-Host "`nГотово!" -ForegroundColor Green