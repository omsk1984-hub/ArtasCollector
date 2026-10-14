# Завершение работы Artas
# Этот скрипт корректно останавливает все сервисы перед выключением Windows

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Завершение работы Artas..." -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

$ErrorActionPreference = 'SilentlyContinue'

# 1. Остановка fractal writer
Write-Host "1. Остановка fractal writer..." -ForegroundColor Yellow
& "$PSScriptRoot\manage_fractal_writer.ps1" stop CNYRUBF 2>$null
& "$PSScriptRoot\manage_fractal_writer.ps1" stop USDRUBF 2>$null

# 2. Остановка PostgreSQL
Write-Host "2. Остановка PostgreSQL..." -ForegroundColor Yellow
wsl bash -c "cd /mnt/d/work/Artas2026/db && docker compose stop postgres" 2>$null

Write-Host "`nВсе сервисы остановлены." -ForegroundColor Green
Write-Host "Можете закрывать VS Code и выключать компьютер.`n" -ForegroundColor Gray
