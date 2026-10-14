# Удержание WSL-дистрибутива запущенным (фоновый клиент wsl.exe).
#
# ПРОБЛЕМА: WSL гасит дистрибутив, когда завершается последний клиент wsl.exe.
# Обычный `wsl -d Ubuntu` занимает терминал (нужно набрать `exit` для выхода, что
# завершает клиент и гасит ВМ).
#
# РЕШЕНИЕ: запустить клиент `wsl.exe -d Ubuntu -e sleep infinity` как ФОНОВЫЙ
# СКРЫТЫЙ процесс (`Start-Process -WindowStyle Hidden`). Он удерживает ВМ,
# НЕ занимая окно PowerShell, НЕ создавая видимого окна консоли wsl.exe (которое,
# если его закрыть, убило бы удержание), и НЕ блокирует обычный доступ
# `wsl -d Ubuntu` (проверено: доступ по-прежнему работает).
#
# ВАЖНО: не использовать WMI Win32_Process.Create — он создаёт ВИДИМОЕ окно
# консоли wsl.exe; при его закрытии удержание обрывается.
#
# ИСПОЛЬЗОВАНИЕ:
#   .\scripts\wsl_hold.ps1            # запустить удержание (фоновый клиент)
#   .\scripts\wsl_hold.ps1 -Stop      # остановить удержание
#   .\scripts\wsl_hold.ps1 -Status    # проверить состояние

param(
    [switch]$Stop,
    [switch]$Status
)

try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$ErrorActionPreference = 'Stop'

$Distro = "Ubuntu"
$matchCmd = 'sleep infinity'

function Get-HoldProcs {
    Get-CimInstance Win32_Process -Filter "Name='wsl.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -match $matchCmd }
}

if ($Status) {
    $procs = Get-HoldProcs
    if ($procs) {
        Write-Host "Удержание активно. Клиенты wsl.exe:" -ForegroundColor Green
        $procs | Select-Object ProcessId,CreationDate | Format-Table -AutoSize
    } else {
        Write-Host "Удержание НЕ активно (нет фоновых клиентов)." -ForegroundColor Yellow
    }
    Write-Host "`nUptime дистрибутива:" -ForegroundColor Cyan
    wsl -d $Distro -e uptime
    exit 0
}

if ($Stop) {
    $procs = Get-HoldProcs
    if ($procs) {
        $procs | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        Write-Host "Удержание остановлено ($($procs.Count) клиент(ов) завершено)." -ForegroundColor Yellow
    } else {
        Write-Host "Удержание уже не активно." -ForegroundColor Gray
    }
    exit 0
}

# Запуск удержания
# ВАЖНО (проверено экспериментально):
#   * WMI Win32_Process.Create создаёт ВИДИМОЕ окно консоли wsl.exe — при его
#     закрытии пользователем процесс убивается и удержание обрывается. НЕ использовать.
#   * Start-Process -WindowStyle Hidden окна НЕ создаёт (MainWindowHandle=0) и
#     процесс переживает завершение родительского скрипта/задачи. Используем его.
# Дополнительно оборачиваем в cmd /c start /b для надёжной отвязки от родителя.
$existing = Get-HoldProcs
if ($existing) {
    Write-Host "Удержание уже активно ($($existing.Count) клиент(ов)). Ничего не делаю." -ForegroundColor Gray
} else {
    Start-Process wsl.exe -ArgumentList '-d',$Distro,'-e','sleep','infinity' -WindowStyle Hidden
    Start-Sleep -Seconds 3
    $procs = Get-HoldProcs
    if ($procs) {
        Write-Host "OK: удержание запущено (PID: $($procs.ProcessId -join ', '))." -ForegroundColor Green
    } else {
        Write-Host "ВНИМАНИЕ: клиент не запустился." -ForegroundColor Red
    }
}

Write-Host "`nUptime дистрибутива:" -ForegroundColor Cyan
wsl -d $Distro -e uptime
Write-Host "`nОстановить: .\scripts\wsl_hold.ps1 -Stop" -ForegroundColor Gray
