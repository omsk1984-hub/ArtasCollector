# ArtasChart launcher (Avalonia + ScottPlot, .NET 10)
#
# Usage:
#   .\run.ps1                        # build Release + run GUI (в фоне, по умолчанию)
#   .\run.ps1 -Foreground            # запустить в текущем терминале (foreground)
#   .\run.ps1 -Configuration Debug
#   .\run.ps1 -NoBuild               # run without rebuild (fast)
#   .\run.ps1 -CheckDb Si            # DB check only (no GUI), then exit
#   .\run.ps1 -Symbol CNYRUBF        # GUI, sets env ARTAS_DEFAULT_SYMBOL
#   .\run.ps1 -Background            # явно фоновый запуск (поведение по умолчанию)
#   .\run.ps1 -Background -NoBuild   # фоновый запуск без пересборки
#   .\run.ps1 -NoPause               # не ждать нажатия клавиши в конце
#
# Можно открывать сколько угодно окон: каждый запуск создаёт новое окно.
# Если приложение уже запущено, скрипт НЕ пересобирает проект (запущенный
# экземпляр держит DLL из bin\Release), а просто открывает новое окно из
# текущего билда. Пересборка выполняется только когда запущенных окон нет.
#
# По умолчанию приложение стартует отдельным процессом, поэтому закрытие
# текущей консоли не останавливает ArtasChart. Для запуска в текущем
# терминале используйте -Foreground.
#
# Скрипт рассчитан в том числе на запуск двойным кликом / через
# "Выполнить с помощью PowerShell" из проводника. В этом случае explorer
# создаёт отдельное окно powershell.exe, которое закрывается сразу после
# завершения скрипта. Чтобы окно не исчезало мгновенно, перед выходом скрипт
# ждёт нажатия клавиши (отключается флагом -NoPause). При запуске из уже
# открытого терминала (или из CI) пауза не выполняется автоматически.

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$NoBuild,
    [switch]$Background,
    [switch]$Foreground,
    [string]$CheckDb = "",
    [string]$Symbol = "",
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Solution = Join-Path $ScriptDir "ArtasChart.sln"
$Project = Join-Path $ScriptDir "ArtasChart.App\ArtasChart.App.csproj"
$AppDll = Join-Path $ScriptDir "ArtasChart.App\bin\$Configuration\net10.0\ArtasChart.App.dll"

# --- Выход с ожиданием клавиши --------------------------------
# Проблема: при запуске "Выполнить с помощью PowerShell" из проводника окно
# создаётся хостом powershell.exe и закрывается сразу после завершения
# скрипта. Внутренние Read-Host/ReadKey в этом режиме ненадёжны, поэтому
# ожидание реализовано внешним процессом `cmd /c pause` — он гарантированно
# блокируется до нажатия клавиши и не даёт хосту завершиться.
# Пауза отключается флагом -NoPause (например, для автоматических запусков).
function Exit-WithPause([int]$Code) {
    if (-not $NoPause) {
        Write-Host ""
        Write-Host "[run] Нажмите любую клавишу для выхода..." -ForegroundColor DarkGray
        try {
            & cmd.exe /c "pause > nul"
        } catch {
            try { $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown") } catch { }
        }
    }
    exit $Code
}

if (-not (Test-Path $Solution)) {
    Write-Error "Solution not found: $Solution"
    Exit-WithPause 1
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet CLI not found. Install .NET 10 SDK: https://aka.ms/dotnet/download"
    Exit-WithPause 1
}

# --- Найти запущенные окна ------------------------------------
# Пока запущено хотя бы одно окно, файлы из bin\Release заняты процессом, и
# пересборка упадёт с MSB3027. Поэтому сборка выполняется только если окон нет.
function Get-RunningInstances {
    try {
        return @(Get-CimInstance Win32_Process -ErrorAction Stop |
            Where-Object { $_.CommandLine -and $_.CommandLine -match 'ArtasChart\.App\.dll' })
    } catch {
        Write-Host "[run] Не удалось перечислить процессы: $_" -ForegroundColor DarkGray
        return @()
    }
}

$instances = Get-RunningInstances

# --- Build ----------------------------------------------------
# Собираем решение явной командой build (а не через `dotnet run`): запуск
# отдельного процесса из bin-выхлопа ниже не держит сборочный конвейер.
if (-not $NoBuild) {
    if ($instances.Count -gt 0) {
        Write-Host "[run] Уже запущено окон: $($instances.Count) — пересборка пропущена," `
            -ForegroundColor DarkYellow
        Write-Host "[run] чтобы не блокировать их DLL. Открываю новое окно из текущего билда." `
            -ForegroundColor DarkGray
    } else {
        Write-Host "[run] Building solution ($Configuration)..." -ForegroundColor Cyan
        & dotnet build $Solution -c $Configuration
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Build failed with code $LASTEXITCODE"
            Exit-WithPause $LASTEXITCODE
        }
    }
}

# --- DB check (CLI, no GUI) ------------------------------------
if ($CheckDb -ne "") {
    Write-Host "[run] DB check, symbol: $CheckDb" -ForegroundColor Cyan
    & dotnet run --project $Project -c $Configuration --no-build -- --check-db $CheckDb
    Exit-WithPause $LASTEXITCODE
}

# --- Run ------------------------------------------------------
# Запускаем собранный DLL напрямую (а не `dotnet run`): процесс приложения не
# связан со сборочным конвейером. Каждый запуск открывает новое окно.
if (-not (Test-Path $AppDll)) {
    Write-Error "App assembly not found: $AppDll (соберите проект: уберите -NoBuild и закройте запущенные окна)"
    Exit-WithPause 1
}

if ($Symbol -ne "") {
    $env:ARTAS_DEFAULT_SYMBOL = $Symbol
    Write-Host "[run] Default symbol: $Symbol" -ForegroundColor Yellow
}

$appArgs = @($AppDll)

# --- Background mode ------------------------------------------
# По умолчанию запускаем в фоне; -Foreground отключает фоновый режим.
$runBackground = -not $Foreground
if ($runBackground) {
    Write-Host "[run] Starting ArtasChart ($Configuration) в фоне..." -ForegroundColor Green

    # Запускаем отдельным процессом, отвязанным от текущей консоли, поэтому
    # закрытие консоли не останавливает приложение. -WindowStyle Hidden
    # предотвращает появление отдельного окна консоли dotnet.
    $proc = Start-Process -FilePath "dotnet" `
        -ArgumentList $appArgs `
        -WorkingDirectory $ScriptDir `
        -WindowStyle Hidden `
        -PassThru

    Write-Host "[run] ArtasChart запущен в фоне (PID $($proc.Id))." -ForegroundColor Green
    Write-Host "[run] Закрытие этой консоли не остановит приложение." -ForegroundColor DarkGray

    Exit-WithPause 0
}

# --- Foreground mode ------------------------------------------
Write-Host "[run] Starting ArtasChart ($Configuration) в текущем терминале..." -ForegroundColor Green
$exitCode = 0
try {
    & dotnet @appArgs
    $exitCode = $LASTEXITCODE
} catch {
    Write-Host "[run] Ошибка запуска: $_" -ForegroundColor Red
    $exitCode = 1
}

Exit-WithPause $exitCode
