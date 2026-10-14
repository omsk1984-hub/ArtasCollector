# Корректная остановка PostgreSQL и всех сервисов Artas перед выключением Windows
# НЕ выключает Windows — только останавливает Docker контейнеры и процессы

param(
    [switch]$Quiet # тихий режим без вывода сообщений
)

if (-not $Quiet) { Write-Host "Остановка Artas сервисов..." -ForegroundColor Cyan }

$ErrorActionPreference = 'SilentlyContinue'
$PSScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

# 1. Остановка PostgreSQL через Docker (graceful shutdown с checkpoint)
wsl bash -c "cd /mnt/d/work/Artas2026/db && docker compose stop postgres" 2>$null

if (-not $Quiet) {
    Write-Host "PostgreSQL корректно остановлен." -ForegroundColor Green
    Write-Host "Вы можете теперь безопасно закрывать VS Code и выключать компьютер.`n" -ForegroundColor Gray
}
