# Добавление автозапуска удержания WSL в задачу AutoStart WSL Ubuntu.
#
# ПРОБЛЕМА: после перезагрузки контейнеры поднимаются сами (задача AutoStart WSL Ubuntu
# + wsl-artas-autostart.service), но удержание ВМ (wsl_hold.ps1) не запускается, поэтому
# ВМ продолжает гаснуть в паузах без wsl-вызовов.
#
# РЕШЕНИЕ: добавить ВТОРОЕ действие в существующую задачу AutoStart WSL Ubuntu —
# запуск scripts\wsl_hold.ps1 (фоновый клиент wsl.exe -e sleep infinity).
# Задача имеет LogonTrigger (Delay PT30S) и выполняется от текущего пользователя,
# поэтому клиент создаётся В ИНТЕРАКТИВНОЙ СЕССИИ (не монополизирует дистрибутив).
#
# ИСПОЛЬЗОВАНИЕ:
#   .\scripts\register_wsl_hold_task.ps1            # добавить/обновить действие
#   .\scripts\register_wsl_hold_task.ps1 -Remove    # убрать добавленное действие
#   .\scripts\register_wsl_hold_task.ps1 -Status    # показать действия задачи

param(
    [switch]$Remove,
    [switch]$Status
)

try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$ErrorActionPreference = 'Stop'

$TaskName   = "AutoStart WSL Ubuntu"
$repoRoot   = (Get-Location).Path
$holdScript = Join-Path $repoRoot "scripts\wsl_hold.ps1"

function Show-Actions {
    $t = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if (-not $t) { Write-Host "Задача '$TaskName' не найдена." -ForegroundColor Red; return }
    Write-Host "Действия задачи '$TaskName':" -ForegroundColor Cyan
    $i = 1
    foreach ($a in $t.Actions) {
        Write-Host ("  [$i] $($a.Execute) $($a.Arguments)") -ForegroundColor White
        $i++
    }
}

if ($Status) {
    Show-Actions
    Write-Host "`nСтатус удержания:" -ForegroundColor Cyan
    & (Join-Path $repoRoot "scripts\wsl_hold.ps1") -Status
    exit 0
}

# Проверяем наличие файла скрипта удержания
if (-not (Test-Path $holdScript)) {
    Write-Host "Не найден скрипт: $holdScript" -ForegroundColor Red
    exit 1
}

$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if (-not $task) {
    Write-Host "Задача '$TaskName' не найдена. Создайте её сначала." -ForegroundColor Red
    exit 1
}

# ВАЖНО: используем pwsh.exe (PowerShell 7), а не powershell.exe (5.1).
# Windows PowerShell 5.1 читает .ps1 без BOM как ANSI/CP866 и падает на кириллице
# в строках (Result=1). pwsh читает UTF-8 корректно. Указываем полный путь,
# т.к. PATH в контексте задачи может не содержать pwsh.
$pwshExe = Join-Path $env:ProgramFiles 'PowerShell\7\pwsh.exe'
if (-not (Test-Path $pwshExe)) { $pwshExe = 'pwsh.exe' }

$holdAction = New-ScheduledTaskAction -Execute $pwshExe `
    -Argument ('-NoProfile -ExecutionPolicy Bypass -File "{0}"' -f $holdScript)

# ВАЖНО: задача зарегистрирована с LogonType=Password, поэтому Set-ScheduledTask
# требует пароль ("Неверное имя пользователя или пароль"). Пароля нет, поэтому
# пересоздаём задачу через Register-ScheduledTask -Force, сохраняя триггеры и
# действия, но с principal Interactive (запуск в интерактивной сессии пользователя,
# пароль не требуется). Это именно то, что нужно — клиент создаётся в сессии юзера.
$currentUser = "$env:USERDOMAIN\$env:USERNAME"
$principal = New-ScheduledTaskPrincipal -UserId $currentUser `
    -LogonType Interactive -RunLevel Highest

if ($Remove) {
    $kept = @($task.Actions | Where-Object { $_.Arguments -notmatch 'wsl_hold\.ps1' })
    if ($kept.Count -eq $task.Actions.Count) {
        Write-Host "Действие удержания не найдено, нечего удалять." -ForegroundColor Gray
        exit 0
    }
    Register-ScheduledTask -TaskName $TaskName -Action $kept -Trigger $task.Triggers `
        -Principal $principal -Settings $task.Settings -Force | Out-Null
    Write-Host "Действие удержания удалено из задачи." -ForegroundColor Yellow
    Show-Actions
    exit 0
}

# Формируем итоговый список действий (старые без wsl_hold + новое)
$kept = @($task.Actions | Where-Object { $_.Arguments -notmatch 'wsl_hold\.ps1' })
$newActions = $kept + $holdAction

Register-ScheduledTask -TaskName $TaskName -Action $newActions -Trigger $task.Triggers `
    -Principal $principal -Settings $task.Settings -Force | Out-Null

Write-Host "Действие удержания добавлено в задачу '$TaskName'." -ForegroundColor Green
Show-Actions
Write-Host "`nПроверить: .\scripts\register_wsl_hold_task.ps1 -Status" -ForegroundColor Gray
Write-Host "Убрать:    .\scripts\register_wsl_hold_task.ps1 -Remove" -ForegroundColor Gray
