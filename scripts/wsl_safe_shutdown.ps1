# Корректная остановка PostgreSQL и WSL перед закрытием Windows
# 1. Сначала останавливает Docker/PostgreSQL (graceful shutdown)
# 2. Потом только закрывает WSL
# НЕ закрывает Windows — только останавливает сервисы + WSL

param(
    [switch]$StopWSL, # закрыть WSL после остановки сервисов
    [switch]$Quiet     # тихий режим без вывода сообщений
)

$PSScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not $Quiet) {
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "Корректная остановка Artas + WSL" -ForegroundColor Cyan
    Write-Host "========================================`n" -ForegroundColor Cyan
}

$ErrorActionPreference = 'SilentlyContinue'

# === ШАГ 1: Остановка PostgreSQL через Docker (graceful shutdown) ===
Write-Host "[1/3] Остановка PostgreSQL..." -ForegroundColor Yellow
wsl bash -c "cd /mnt/d/work/Artas2026/db && docker compose stop postgres" 2>$null

Start-Sleep -Seconds 3

# Проверяем статус
$status = wsl docker ps --filter name=artas_postgres --format "{{.Status}}" 2>$null
if ($status -match "Up") {
    Write-Host "[WARN] PostgreSQL не остановился принудительно..." -ForegroundColor Red
    Write-Host "  Повторная попытка..." -ForegroundColor Yellow
    wsl bash -c "docker stop -t 5 artas_postgres" 2>$null
    Start-Sleep -Seconds 5
} else {
    Write-Host "PostgreSQL корректно остановлен." -ForegroundColor Green
}

# === ШАГ 2: Остановка fractal writer процессов ===
Write-Host "`n[2/3] Остановка fractal writer..." -ForegroundColor Yellow
& "$PSScriptRoot\manage_fractal_writer.ps1" stop CNYRUBF 2>$null
& "$PSScriptRoot\manage_fractal_writer.ps1" stop USDRUBF 2>$null

# === ШАГ 3: Закрытие WSL (опционально) ===
if ($StopWSL) {
    Write-Host "`n[3/3] Закрытие WSL..." -ForegroundColor Yellow
    wsl --shutdown
    Write-Host "WSL остановлен." -ForegroundColor Green
}

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "Все сервисы остановлены!" -ForegroundColor Green
Write-Host "Если WSL был закрыт, запустите:`n   wsl" -ForegroundColor Gray
Write-Host "========================================`n" -ForegroundColor Gray
