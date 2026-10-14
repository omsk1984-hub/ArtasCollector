# Установка systemd-сервисов внутри WSL для управления жизненным циклом Artas
# при выключении/загрузке WSL-ВМ.
#
# Устанавливает ДВА сервиса:
#   1. wsl-docker-graceful-shutdown.service — при halt/reboot/shutdown делает
#      `docker stop -t 60 artas_postgres` (graceful checkpoint, чистый WAL).
#   2. wsl-artas-autostart.service — при загрузке ВМ поднимает artas_postgres
#      (docker stop при выключении => Docker не поднимает контейнер сам).
#
# Использование:
#   .\scripts\install_wsl_graceful_shutdown.ps1            # установить
#   .\scripts\install_wsl_graceful_shutdown.ps1 -Uninstall # удалить
#   .\scripts\install_wsl_graceful_shutdown.ps1 -Status    # проверить

param(
    [switch]$Uninstall,
    [switch]$Status
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Get-Location).Path

# Относительные WSL-пути к исходникам (репозиторий на /mnt/d/...)
$wslInitDir = "/mnt/" + $repoRoot.Substring(0,1).ToLower() + $repoRoot.Substring(2).Replace('\','/') + "/db/init-scripts"

$stopScript = "/usr/local/bin/artas-graceful-stop.sh"
$stopService = "/etc/systemd/system/wsl-docker-graceful-shutdown.service"
$startScript = "/usr/local/bin/artas-autostart.sh"
$startService = "/etc/systemd/system/wsl-artas-autostart.service"

$unitStop = "wsl-docker-graceful-shutdown.service"
$unitStart = "wsl-artas-autostart.service"

function Invoke-Wsl {
    param([string]$Cmd)
    wsl -d Ubuntu -u root bash -c $Cmd
}

if ($Status) {
    Write-Host "Статус сервисов Artas в WSL:" -ForegroundColor Cyan
    Invoke-Wsl "for u in $unitStop $unitStart; do echo \"--- \$u ---\"; systemctl is-enabled \$u 2>&1; systemctl is-active \$u 2>&1; done"
    exit 0
}

if ($Uninstall) {
    Write-Host "Удаление systemd-сервисов Artas из WSL..." -ForegroundColor Cyan
    Invoke-Wsl "systemctl disable $unitStop $unitStart 2>/dev/null; rm -f $stopService $startService $stopScript $startScript; systemctl daemon-reload"
    Write-Host "Сервисы удалены." -ForegroundColor Green
    exit 0
}

Write-Host "Установка systemd-сервисов Artas в WSL..." -ForegroundColor Cyan

# Проверяем, что внутри WSL включён systemd
$systemd = (wsl -d Ubuntu -u root bash -c "ps -p 1 -o comm= 2>/dev/null").Trim()
if ($systemd -ne "systemd") {
    Write-Host "ВНИМАНИЕ: внутри WSL PID 1 = '$systemd', а не systemd." -ForegroundColor Red
    Write-Host "Добавьте в /etc/wsl.conf секцию [boot] systemd=true и выполните 'wsl --shutdown'." -ForegroundColor Yellow
    exit 1
}

Write-Host "Копирование файлов в WSL..." -ForegroundColor Yellow
Invoke-Wsl "cp '$wslInitDir/artas-graceful-stop.sh' $stopScript && chmod +x $stopScript"
Invoke-Wsl "cp '$wslInitDir/wsl-docker-graceful-shutdown.service' $stopService"
Invoke-Wsl "cp '$wslInitDir/artas-autostart.sh' $startScript && chmod +x $startScript"
Invoke-Wsl "cp '$wslInitDir/wsl-artas-autostart.service' $startService"

Write-Host "Включение сервисов..." -ForegroundColor Yellow
Invoke-Wsl "systemctl daemon-reload && systemctl enable $unitStop && systemctl enable $unitStart"

Write-Host "`nПроверка:" -ForegroundColor Cyan
Invoke-Wsl "for u in $unitStop $unitStart; do echo \"--- \$u ---\"; systemctl is-enabled \$u; done"

Write-Host "`n✅ Сервисы установлены и включены." -ForegroundColor Green
Write-Host "  При выключении ВМ:  $stopScript  → docker stop artas_postgres (graceful WAL)" -ForegroundColor White
Write-Host "  При загрузке ВМ:    $startScript → docker compose up -d postgres" -ForegroundColor White
Write-Host "`nПроверить:   .\scripts\install_wsl_graceful_shutdown.ps1 -Status" -ForegroundColor Gray
