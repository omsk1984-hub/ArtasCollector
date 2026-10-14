# ArtasTools.ReportAnalyzer launcher
#
# Анализирует JSON-отчёты ArtasTools.MemReport из reports/ и формирует сводку.
#
# Использование:
#   .\run_ReportAnalyzer.ps1                       # все отчёты, вывод в reports/
#   .\run_ReportAnalyzer.ps1 -LastHours 24         # только отчёты за последние 24ч
#   .\run_ReportAnalyzer.ps1 -Print                # вывести Markdown в консоль
#   .\run_ReportAnalyzer.ps1 -NoBuild              # без пересборки
#
# Коды возврата: 0 — OK, 2 — CRITICAL (проблемы), 1 — ошибка.

param(
    [string]$ReportsDir = "",
    [string]$OutputDir = "",
    [int]$LastHours = 0,
    [double]$MinR2 = 0.5,
    [ValidateSet("Markdown", "Json", "Both")]
    [string]$Format = "Both",
    [switch]$Print,
    [switch]$NoBuild,
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $ScriptDir "ArtasTools.ReportAnalyzer\ArtasTools.ReportAnalyzer.csproj"
# Корень репозитория (src/Tools/ReportAnalyzer -> ../../..) — каталог отчётов по умолчанию.
$ProjectRoot = Resolve-Path (Join-Path $ScriptDir "..\..\..")
if ([string]::IsNullOrWhiteSpace($ReportsDir)) {
    $ReportsDir = Join-Path $ProjectRoot "reports"
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
    "--reports-dir", $ReportsDir,
    "--min-r2", $MinR2
)
$appArgs += @("--format", $Format.ToLower())
if ($LastHours -gt 0) { $appArgs += @("--last-hours", $LastHours) }
if ($OutputDir)       { $appArgs += @("--output-dir", $OutputDir) }
if ($Print)           { $appArgs += "--print" }

if ($NoBuild) {
    # Запуск уже собранной сборки напрямую (быстрее).
    $dll = Join-Path $ScriptDir "ArtasTools.ReportAnalyzer\bin\Release\net10.0\ArtasTools.ReportAnalyzer.dll"
    if (-not (Test-Path $dll)) {
        $dll = Join-Path $ScriptDir "ArtasTools.ReportAnalyzer\bin\Debug\net10.0\ArtasTools.ReportAnalyzer.dll"
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
