# Безопасная остановка всех сервисов Artas перед закрытием
# Использование: .\safe_shutdown.ps1

Write-Host "Остановка всех сервисов..." -ForegroundColor Cyan

# 1. Останавливаем fractal writer'ы
Write-Host "`n1. Остановка fractal writer..." -ForegroundColor Yellow
& "$PSScriptRoot\_CNYRUBF_stop.ps1" 2>$null
& "$PSScriptRoot\_USDRUBF_stop.ps1" 2>$null

# 2. Останавливаем PostgreSQL в Docker
Write-Host "`n2. Остановка PostgreSQL..." -ForegroundColor Yellow
wsl bash -c "cd /mnt/d/work/Artas2026/db && docker compose stop" 2>$null

# 3. Опционально: закрыть WSL
if ($args[0] -eq "--wsl-shutdown") {
    Write-Host "`n3. Закрытие WSL..." -ForegroundColor Yellow
    wsl --shutdown
    Write-Host "WSL остановлен." -ForegroundColor Green
}

Write-Host "`nВсе сервисы остановлены. Можно закрывать VS Code." -ForegroundColor Green
