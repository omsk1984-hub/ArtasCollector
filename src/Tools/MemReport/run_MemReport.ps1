# ArtasTools.MemReport launcher
#
# Формирует отчёт по утечке памяти из Prometheus и сохраняет его в reports/.
#
# Использование:
#   .\run_MemReport.ps1                          # окно 12ч, фильтр artas-writer
#   .\run_MemReport.ps1 -Hours 24                # окно 24ч
#   .\run_MemReport.ps1 -Job "" -Hours 6         # без фильтра (все сервисы)
#   .\run_MemReport.ps1 -Print                   # вывести Markdown в консоль
#   .\run_MemReport.ps1 -NoBuild                 # без пересборки
#
# Коды возврата: 0 — OK, 2 — CRITICAL (утечка), 1 — ошибка.

param(
    [int]$Hours = 12,
    [string]$Step = "60s",
    [string]$Job = "artas-writer",
    [string]$PrometheusUrl = "http://localhost:9090",
    # Пороги достоверности серии: короткие стартовые серии исключаются из вердикта.
    [int]$MinSamples = 30,
    [int]$MinDurationMinutes = 60,
    [ValidateSet("Markdown", "Json", "Both")]
    [string]$Format = "Both",
    [string]$OutputDir = "",
    [switch]$Print,
    [switch]$NoBuild,
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
# Скрипт лежит внутри проекта MemReport (src/Tools/MemReport).
$Project = Join-Path $ScriptDir "ArtasTools.MemReport\ArtasTools.MemReport.csproj"
# Корень репозитория (src/Tools/MemReport -> ../../..) — каталог отчётов по умолчанию.
$ProjectRoot = Resolve-Path (Join-Path $ScriptDir "..\..\..")
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $ProjectRoot "reports"
}

function Exit-WithPause([int]$Code) {
    if (-not $NoPause) {
        Write-Host ""
        Write-Host "[run] Нажмите любую клавишу для выхода..." -ForegroundColor DarkGray
        try { & cmd.exe /c "pause > nul" } catch { try { $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown") } catch { } }
    }
    exit $Code
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $dotnet = "C:\Program Files\dotnet\dotnet.exe"
    if (-not (Test-Path $dotnet)) {
        Write-Error "dotnet CLI не найден. Требуется .NET 10 SDK."
        Exit-WithPause 1
    }
} else {
    $dotnet = "dotnet"
}

# Формируем аргументы для dotnet run.
$appArgs = @(
    "--prometheus-url", $PrometheusUrl,
    "--hours", $Hours,
    "--step", $Step,
    "--job", $Job,
    "--min-samples", $MinSamples,
    "--min-duration-minutes", $MinDurationMinutes
)
$appArgs += @("--format", $Format.ToLower())
if ($OutputDir) { $appArgs += @("--output-dir", $OutputDir) }
if ($Print)     { $appArgs += "--print" }

if ($NoBuild) {
    # Запуск уже собранной сборки напрямую (быстрее).
    $dll = Join-Path $ScriptDir "ArtasTools.MemReport\bin\Release\net10.0\ArtasTools.MemReport.dll"
    if (-not (Test-Path $dll)) {
        $dll = Join-Path $ScriptDir "ArtasTools.MemReport\bin\Debug\net10.0\ArtasTools.MemReport.dll"
    }
    if (-not (Test-Path $dll)) {
        Write-Error "Сборка не найдена: $dll. Запустите без -NoBuild."
        Exit-WithPause 1
    }
    & $dotnet $dll @appArgs
} else {
    & $dotnet run --project $Project -- @appArgs
}

$code = $LASTEXITCODE
Exit-WithPause $code
