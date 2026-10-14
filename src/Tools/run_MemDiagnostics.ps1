# ArtasTools — общий запуск диагностики памяти
#
# Последовательно выполняет:
#   1) MemReport      — формирование отчёта по памяти из Prometheus в reports/;
#   2) ReportAnalyzer — анализ сформированных JSON-отчётов и сводка проблем.
#
# Использование:
#   .\run_MemDiagnostics.ps1                       # окно 12ч, фильтр artas-writer
#   .\run_MemDiagnostics.ps1 -Hours 24             # большее окно анализа
#   .\run_MemDiagnostics.ps1 -Job "" -Hours 12     # без фильтра (все сервисы)
#   .\run_MemDiagnostics.ps1 -LastHours 48         # анализировать отчёты за 48ч
#   .\run_MemDiagnostics.ps1 -Print                # Markdown обеих стадий в консоль
#   .\run_MemDiagnostics.ps1 -SkipMemReport        # только анализ готовых отчётов
#   .\run_MemDiagnostics.ps1 -SkipAnalyzer         # только сбор отчёта
#
# Коды возврата: 0 — OK, 2 — CRITICAL (утечка/проблемы), 1 — ошибка.

param(
    [int]$Hours = 12,
    [string]$Step = "60s",
    [string]$Job = "artas-writer",
    [string]$PrometheusUrl = "http://localhost:9090",
    [int]$LastHours = 0,
    [double]$MinR2 = 0.5,
    # Пороги достоверности серии (короткие стартовые серии исключаются из вердикта).
    [int]$MinSamples = 30,
    [int]$MinDurationMinutes = 60,
    [switch]$Print,
    [switch]$NoBuild,
    [switch]$SkipMemReport,
    [switch]$SkipAnalyzer,
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$MemReportScript = Join-Path $ScriptDir "MemReport\run_MemReport.ps1"
$AnalyzerScript = Join-Path $ScriptDir "ReportAnalyzer\run_ReportAnalyzer.ps1"

function Exit-WithPause([int]$Code) {
    if (-not $NoPause) {
        Write-Host ""
        Write-Host "[run] Нажмите любую клавишу для выхода..." -ForegroundColor DarkGray
        try { & cmd.exe /c "pause > nul" } catch { try { $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown") } catch { } }
    }
    exit $Code
}

foreach ($script in @($MemReportScript, $AnalyzerScript)) {
    if (-not (Test-Path $script)) {
        Write-Error "Не найден скрипт: $script"
        Exit-WithPause 1
    }
}

# Общие переключатели для вложенных скриптов: -NoPause обязателен, чтобы они не
# останавливались на паузе внутри составного запуска.
$commonFlags = @{ NoPause = $true }
if ($NoBuild) { $commonFlags.NoBuild = $true }
if ($Print)   { $commonFlags.Print   = $true }

$memCode = 0
$analyzerCode = 0

# --- Стадия 1: MemReport ---
if (-not $SkipMemReport) {
    Write-Host ""
    Write-Host "=== [1/2] Формирование отчёта по памяти (MemReport) ===" -ForegroundColor Cyan

    $memParams = @{
        Hours              = $Hours
        Step               = $Step
        Job                = $Job
        PrometheusUrl      = $PrometheusUrl
        MinSamples         = $MinSamples
        MinDurationMinutes = $MinDurationMinutes
    } + $commonFlags

    & $MemReportScript @memParams
    $memCode = $LASTEXITCODE
    if ($memCode -eq 1) {
        Write-Error "[MemReport] Ошибка выполнения (код $memCode). Прерываю диагностику."
        Exit-WithPause 1
    }
} else {
    Write-Host "=== [1/2] MemReport пропущен (-SkipMemReport) ===" -ForegroundColor DarkYellow
}

# --- Стадия 2: ReportAnalyzer ---
if (-not $SkipAnalyzer) {
    Write-Host ""
    Write-Host "=== [2/2] Анализ отчётов (ReportAnalyzer) ===" -ForegroundColor Cyan

    $analyzerParams = @{ MinR2 = $MinR2 } + $commonFlags
    if ($LastHours -gt 0) { $analyzerParams.LastHours = $LastHours }

    & $AnalyzerScript @analyzerParams
    $analyzerCode = $LASTEXITCODE
    if ($analyzerCode -eq 1) {
        Write-Error "[ReportAnalyzer] Ошибка выполнения (код $analyzerCode)."
        Exit-WithPause 1
    }
} else {
    Write-Host "=== [2/2] ReportAnalyzer пропущен (-SkipAnalyzer) ===" -ForegroundColor DarkYellow
}

# Итоговый код: максимум из кодов стадий (2 = CRITICAL важнее 0).
$finalCode = [Math]::Max($memCode, $analyzerCode)
Write-Host ""
Write-Host "=== Диагностика завершена (код $finalCode) ===" -ForegroundColor Cyan
Exit-WithPause $finalCode
