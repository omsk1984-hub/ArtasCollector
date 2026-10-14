# Регистрация задачи автовыполнения graceful_pg_shutdown.ps1 при выключении/перезагрузке Windows.
#
# ВАЖНО: основной механизм graceful shutdown обеспечивается systemd-сервисом
# внутри WSL (wsl-docker-graceful-shutdown.service, устанавливается скриптом
# install_wsl_graceful_shutdown.ps1). Эта Windows-задача — дополнительная страховка:
# она срабатывает раньше, чем Windows начнёт гасить WSL, и успевает остановить PG.
#
# Использование:
#   .\scripts\register_shutdown_task.ps1              # зарегистрировать/обновить задачу
#   .\scripts\register_shutdown_task.ps1 -Unregister  # удалить задачу
#   .\scripts\register_shutdown_task.ps1 -Status      # показать состояние

param(
    [switch]$Unregister,
    [switch]$Status
)

$TaskName = "Artas_SafeShutdown"
$ScriptPath = Join-Path (Get-Location).Path "scripts\graceful_pg_shutdown.ps1"

if ($Status) {
    $t = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($t) {
        $info = Get-ScheduledTaskInfo -TaskName $TaskName
        Write-Host "Задача '$TaskName': State=$($t.State) LastRun=$($info.LastRunTime) Result=$($info.LastTaskResult)" -ForegroundColor Cyan
    } else {
        Write-Host "Задача '$TaskName' не зарегистрирована." -ForegroundColor Yellow
    }
    exit 0
}

if ($Unregister) {
    Write-Host "Удаление задачи автовыполнения при выключении..." -ForegroundColor Cyan
    try {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction Stop
        Write-Host "Задача '$TaskName' удалена." -ForegroundColor Yellow
    } catch {
        Write-Host "Задача '$TaskName' не найдена или уже удалена." -ForegroundColor Gray
    }
    exit 0
}

if (-not (Test-Path $ScriptPath)) {
    throw "Не найден скрипт: $ScriptPath"
}

Write-Host "Регистрация задачи безопасной остановки при выключении..." -ForegroundColor Cyan

# Удаляем старую задачу если есть, чтобы не плодить дубликаты
Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue

# Триггер на событие завершения работы/перезагрузки:
#   EventID 1074 — инициировано плановое завершение (пользователем/процессом)
#   EventID 6006 — "The clean shutdown" (лог EventLog)
# Корректный XPath: вложенный System внутри *[System[...]] БЕЗ лишних скобок.
$subscription = @'
<QueryList>
  <Query Id="0" Path="System">
    <Select Path="System">*[System[Provider[@Name='User32'] and (EventID=1074)]]</Select>
  </Query>
</QueryList>
'@

# Экранируем XML для вставки в <Subscription>
$subscriptionEscaped = [System.Security.SecurityElement]::Escape($subscription)

$taskXml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>Безопасная остановка PostgreSQL и всех сервисов Artas перед выключением Windows</Description>
  </RegistrationInfo>
  <Triggers>
    <EventTrigger>
      <Enabled>true</Enabled>
      <Subscription>$subscriptionEscaped</Subscription>
    </EventTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <GroupId>S-1-5-18</GroupId>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <ExecutionTimeLimit>PT2M</ExecutionTimeLimit>
    <Priority>4</Priority>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>powershell.exe</Command>
      <Arguments>-NoProfile -ExecutionPolicy Bypass -File "$ScriptPath" -Quiet</Arguments>
    </Exec>
  </Actions>
</Task>
"@

$xmlPath = Join-Path $env:TEMP "artas_shutdown_task.xml"
$taskXml | Out-File -FilePath $xmlPath -Encoding Unicode

$result = schtasks /create /tn $TaskName /xml $xmlPath /f
Remove-Item $xmlPath -ErrorAction SilentlyContinue

if ($LASTEXITCODE -ne 0) {
    Write-Host "Не удалось зарегистрировать задачу: $result" -ForegroundColor Red
    exit 1
}

Write-Host "`n✅ Задача '$TaskName' создана." -ForegroundColor Green
Write-Host "Триггер: событие System EventID=1074 (плановое завершение работы)." -ForegroundColor Gray
Write-Host "Действие: $ScriptPath -Quiet" -ForegroundColor Gray
Write-Host "" -ForegroundColor Gray
Write-Host "Проверить:   .\scripts\register_shutdown_task.ps1 -Status" -ForegroundColor Gray
Write-Host "Удалить:     .\scripts\register_shutdown_task.ps1 -Unregister" -ForegroundColor Gray
