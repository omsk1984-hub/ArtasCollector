<#
.SYNOPSIS
    Kill processes by port numbers.
.DESCRIPTION
    Finds processes listening on specified ports and terminates them.
.PARAMETER Ports
    Array of port numbers to kill processes on.
.PARAMETER Force
    Force kill without confirmation prompt.
.EXAMPLE
    .\kill_by_ports.ps1 -Ports 5006
    .\kill_by_ports.ps1 -Ports 5006,5007
    .\kill_by_ports.ps1 -Ports 5006 -Force
#>

param(
    [Parameter(ValueFromRemainingArguments)]
    [int[]]$Ports = @(5006),

    [switch]$Force
)

function Stop-ProcessByPort {
    param([int]$Port)

    # Находим PID процесса, слушающего указанный порт
    $connection = netstat -ano |
        Select-String ":$Port\s" |
        Where-Object { $_ -match 'LISTENING' } |
        Select-Object -First 1

    if (-not $connection) {
        Write-Host "Порт $Port не занят." -ForegroundColor Yellow
        return
    }

    # Извлекаем PID
    $parts = $connection -split '\s+'
    $pid = $parts[-1]

    Write-Host "Порт $Port -> PID: $pid" -ForegroundColor Cyan

    # Если не Force — запрашиваем подтверждение
    if (-not $Force) {
        $confirm = Read-Host "Завершить процесс с PID $pid? (y/n)"
        if ($confirm -ne 'y') {
            Write-Host "Пропускаем PID $pid" -ForegroundColor Gray
            return
        }
    }

    try {
        Stop-Process -Id $pid -Force
        Write-Host "Процесс PID $pid (порт $Port) завершён." -ForegroundColor Green
    }
    catch {
        Write-Host "Ошибка при завершении PID $pid на порту ${Port}: $_" -ForegroundColor Red
    }
}

Write-Host "Поиск процессов на портах: $($Ports -join ', ')" -ForegroundColor Magenta

foreach ($port in $Ports) {
    Stop-ProcessByPort -Port $port
}

Write-Host "Готово." -ForegroundColor Magenta
if (-not $Force) {
    # Read-Host "Нажмите Enter для выхода..."
}
