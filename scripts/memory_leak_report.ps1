#Requires -Version 5.0
<#
.SYNOPSIS
    Формирование отчёта по утечке памяти из Prometheus (Artas2026).

.DESCRIPTION
    Скрипт опрашивает HTTP API Prometheus (TSDB, куда OTel Collector пишет
    .NET runtime-метрики через remote-write) и формирует отчёт о состоянии
    памяти приложения: рост managed heap, работа GC, RSS процесса.

    Логика выявления утечки:
      • Берётся диапазон в "окне анализа" (по умолчанию последние 6 часов).
      • Для каждой метрики считается линейный тренд (наклон, МБ/час).
      • Утечкой считается устойчивый рост managed heap / RSS при
        незначительном падении после сборок GC.
      • Итоговая оценка — по комбинации трендов + число сборок Gen2.

    Отчёт сохраняется в каталог reports/ в форматах Markdown и JSON.

.PARAMETER PrometheusUrl
    Базовый URL Prometheus API. По умолчанию http://localhost:9090
    (порт проброшен из WSL: monitoring/docker-compose.monitoring.yml).

.PARAMETER Hours
    Глубина окна анализа в часах (по умолчанию 6).

.PARAMETER Step
    Шаг выборки для range-запросов (по умолчанию 60s).

.PARAMETER OutputDir
    Каталог для отчётов (по умолчанию <корень>/reports).

.PARAMETER Job
    Фильтр по job/service в метках. По умолчанию "artas-writer".
    Пустая строка — не фильтровать.

.PARAMETER Format
    Форматы вывода: Markdown, Json, Both (по умолчанию Both).

.EXAMPLE
    .\scripts\memory_leak_report.ps1
    .\scripts\memory_leak_report.ps1 -Hours 24 -OutputDir .\reports
    .\scripts\memory_leak_report.ps1 -PrometheusUrl http://localhost:9090 -Format Markdown
#>

[CmdletBinding()]
param(
    [string]$PrometheusUrl = "http://localhost:9090",
    [int]$Hours = 6,
    [string]$Step = "60s",
    [string]$OutputDir = "",
    [string]$Job = "artas-writer",
    [ValidateSet("Markdown", "Json", "Both")]
    [string]$Format = "Both"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Resolve-Path (Join-Path $ScriptDir "..")
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $ProjectRoot "reports"
}
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

function Write-Info { param($Msg) Write-Host "[MEM-REPORT] $Msg" -ForegroundColor Cyan }
function Write-Ok   { param($Msg) Write-Host "[OK] $Msg" -ForegroundColor Green }
function Write-Warn { param($Msg) Write-Host "[WARN] $Msg" -ForegroundColor Yellow }
function Write-Err  { param($Msg) Write-Host "[ERROR] $Msg" -ForegroundColor Red }

# region: Prometheus HTTP API helpers

# Базовый GET к Prometheus API. Возвращает распарсенный JSON или $null.
function Invoke-PromQuery {
    param([string]$Path, [hashtable]$Params)

    $pairs = @()
    foreach ($k in $Params.Keys) {
        $v = [string]$Params[$k]
        $pairs += ("{0}={1}" -f $k, [uri]::EscapeDataString($v))
    }
    $url = "{0}{1}?{2}" -f $PrometheusUrl.TrimEnd('/'), $Path, ($pairs -join "&")

    try {
        $resp = Invoke-RestMethod -Uri $url -Method Get -TimeoutSec 15
        return $resp
    } catch {
        Write-Warn "Запрос не удался: $url — $_"
        return $null
    }
}

# Мгновенный запрос (instant vector): возвращает список @{ labels; value }
function Invoke-PromInstant {
    param([string]$Query)
    $resp = Invoke-PromQuery -Path "/api/v1/query" -Params @{ query = $Query }
    if ($null -eq $resp -or $resp.status -ne "success") { return @() }
    $out = @()
    foreach ($r in $resp.data.result) {
        $out += [pscustomobject]@{
            Labels = $r.metric
            Value  = [double]$r.value[1]
        }
    }
    return $out
}

# Range-запрос: возвращает серию @{ labels; points = @( @{t; v} ) }
function Invoke-PromRange {
    param([string]$Query, [int]$Hours, [string]$Step)
    $end = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $start = $end - ($Hours * 3600)
    $resp = Invoke-PromQuery -Path "/api/v1/query_range" -Params @{
        query = $Query
        start = $start
        end   = $end
        step  = $Step
    }
    if ($null -eq $resp -or $resp.status -ne "success") { return @() }
    $out = @()
    foreach ($r in $resp.data.result) {
        $points = @()
        foreach ($p in $r.values) {
            $points += [pscustomobject]@{ T = [double]$p[0]; V = [double]$p[1] }
        }
        $out += [pscustomobject]@{ Labels = $r.metric; Points = $points }
    }
    return $out
}

# endregion

# region: Аналитика

# Линейная регрессия по точкам серии. Возвращает наклон (ед./час) и R^2.
function Get-SeriesTrend {
    param($Points)

    $n = $Points.Count
    if ($n -lt 2) {
        return [pscustomobject]@{ SlopePerHour = 0.0; R2 = 0.0; Samples = $n;
            First = 0.0; Last = 0.0; Min = 0.0; Max = 0.0 }
    }

    $t0 = $Points[0].T
    $xs = @(); $ys = @()
    foreach ($p in $Points) { $xs += (($p.T - $t0) / 3600.0); $ys += $p.V }

    $meanX = ($xs | Measure-Object -Average).Average
    $meanY = ($ys | Measure-Object -Average).Average

    $sxx = 0.0; $sxy = 0.0; $syy = 0.0
    for ($i = 0; $i -lt $n; $i++) {
        $dx = $xs[$i] - $meanX
        $dy = $ys[$i] - $meanY
        $sxx += $dx * $dx
        $sxy += $dx * $dy
        $syy += $dy * $dy
    }

    $slope = if ($sxx -ne 0) { $sxy / $sxx } else { 0.0 }
    $r2 = if ($sxx -ne 0 -and $syy -ne 0) { ($sxy * $sxy) / ($sxx * $syy) } else { 0.0 }

    return [pscustomobject]@{
        SlopePerHour = [double]$slope
        R2           = [double]$r2
        Samples      = $n
        First        = [double]$ys[0]
        Last         = [double]$ys[$n - 1]
        Min          = [double](($ys | Measure-Object -Minimum).Minimum)
        Max          = [double](($ys | Measure-Object -Maximum).Maximum)
    }
}

# endregion

# region: Обнаружение метрик

Write-Info "Prometheus: $PrometheusUrl"
Write-Info "Окно анализа: последние $Hours ч (step=$Step)"

# Проверка доступности Prometheus.
$up = Invoke-PromInstant -Query "up"
if ($null -eq $up) {
    Write-Err "Prometheus недоступен по адресу $PrometheusUrl"
    Write-Info "Запустите стек мониторинга: .\scripts\manage_monitoring.ps1 -Action start"
    exit 1
}
Write-Ok "Prometheus доступен."

# Фильтр по метке. Формируем селектор вида {service_name="..."}.
$labelFilter = ""
if (-not [string]::IsNullOrWhiteSpace($Job)) {
    $labelFilter = '{service_name="' + $Job + '"}'
}

# Кандидаты метрик памяти (.NET runtime instrumentation → OTel → Prometheus).
# Имена после prometheusremotewrite: точки → подчёркивания, +суффиксы типа.
# ВАЖНО: имена соответствуют фактической runtime-инструментации .NET 10
# (System.Runtime Meter); проверены запросом /api/v1/label/__name__/values.
$candidates = @(
    @{ Key = "managed_heap";   Query = "dotnet_gc_last_collection_heap_size_bytes$labelFilter"; Unit = "MB"; Desc = "Managed heap (GC heap size, последняя сборка)" },
    @{ Key = "rss";            Query = "dotnet_process_memory_working_set_bytes$labelFilter"; Unit = "MB"; Desc = "Process working set (RSS)" },
    @{ Key = "gc_committed";   Query = "dotnet_gc_last_collection_memory_committed_size_bytes$labelFilter"; Unit = "MB"; Desc = "Committed heap (последняя сборка)" },
    @{ Key = "gc_fragmentation"; Query = "dotnet_gc_last_collection_heap_fragmentation_size_bytes$labelFilter"; Unit = "MB"; Desc = "Heap fragmentation (последняя сборка)" },
    @{ Key = "alloc_rate";     Query = "rate(dotnet_gc_heap_allocated_bytes_total$labelFilter[$Step])"; Unit = "MB/s"; Desc = "Allocation rate" },
    @{ Key = "gc_collections"; Query = "sum(rate(dotnet_gc_collections_total$labelFilter[$Step])) by (generation)"; Unit = "1/s"; Desc = "GC collections by generation" },
    @{ Key = "gc_pause";       Query = "rate(dotnet_gc_pause_time_seconds_total$labelFilter[$Step])"; Unit = "s/s"; Desc = "GC pause time rate" },
    @{ Key = "threads";        Query = "dotnet_thread_pool_thread_count_total$labelFilter"; Unit = "шт"; Desc = "Thread pool thread count" }
)

$available = @()
$series = @()

foreach ($c in $candidates) {
    $s = Invoke-PromRange -Query $c.Query -Hours $Hours -Step $Step
    if ($s.Count -gt 0) {
        $available += $c
        $series += [pscustomobject]@{ Candidate = $c; Series = $s }
    }
}

if ($available.Count -eq 0) {
    Write-Warn "Не найдено ни одной метрики памяти по фильтру '$Job'."
    Write-Info "Проверьте, что Writer запущен и OTel Collector получает метрики:"
    Write-Info "  http://localhost:9090/api/v1/label/__name__/values"
    Write-Info "Возможные причины: сервис не запущен или remote-write не настроен."
}

# endregion

# region: Построение анализа

$findings = @()

foreach ($item in $series) {
    $c = $item.Candidate
    $isBytes = ($c.Unit -eq "MB")
    foreach ($s in $item.Series) {
        $trend = Get-SeriesTrend -Points $s.Points

        # Пересчёт в МБ/час только для метрик объёма (bytes → MB).
        # Числовые метрики (count/rate, напр. threads) — без нормализации.
        $slopeMbPerHour = if ($isBytes) { $trend.SlopePerHour / 1MB } else { $trend.SlopePerHour }

        $gen = ""
        if ($s.Labels.PSObject.Properties.Name -contains "generation") {
            $gen = [string]$s.Labels.generation
        }
        $inst = ""
        if ($s.Labels.PSObject.Properties.Name -contains "service_instance_id") {
            $inst = [string]$s.Labels.service_instance_id
        }

        # Normalize только для байтовых метрик, иначе — исходные единицы.
        $norm = if ($isBytes) { 1MB } else { 1.0 }

        $findings += [pscustomobject]@{
            Metric       = $c.Key
            Description  = $c.Desc
            Unit         = $c.Unit
            Generation   = $gen
            Instance     = $inst
            Samples      = $trend.Samples
            First        = $trend.First / $norm
            Last         = $trend.Last / $norm
            Min          = $trend.Min / $norm
            Max          = $trend.Max / $norm
            SlopeMbPerHr = $slopeMbPerHour
            R2           = $trend.R2
        }
    }
}

# Оценка вероятности утечки по ключевым метрикам (managed heap / RSS).
function Get-LeakVerdict {
    param($Findings)

    # Суммарный рост managed heap (все поколения) — МБ/час.
    $heap = $Findings | Where-Object { $_.Metric -eq "managed_heap" }
    $rss  = $Findings | Where-Object { $_.Metric -eq "rss" }

    $heapSlope = if ($heap) { ($heap | Measure-Object -Property SlopeMbPerHr -Sum).Sum } else { 0.0 }
    $rssSlope  = if ($rss)  { ($rss  | Measure-Object -Property SlopeMbPerHr -Sum).Sum } else { 0.0 }
    $heapR2    = if ($heap) { ($heap | Measure-Object -Property R2 -Maximum).Maximum } else { 0.0 }

    $level = "OK"; $reason = "Признаков устойчивого роста памяти не обнаружено."

    # Порог: устойчивый рост > 10 МБ/час при хорошем линейном фите (R^2 > 0.6).
    if ($heapSlope -gt 50 -or $rssSlope -gt 100) {
        $level = "CRITICAL"
        $reason = "Высокая скорость роста памяти."
    } elseif ($heapSlope -gt 10 -or $rssSlope -gt 20) {
        $level = "WARNING"
        $reason = "Заметный устойчивый рост памяти — вероятная утечка."
    }

    if ($heapR2 -lt 0.5 -and $level -ne "OK") {
        $reason += " Линейный фит слабый (R^2=$([math]::Round($heapR2,2))) — требуется больше данных."
    }

    return [pscustomobject]@{
        Level      = $level
        Reason     = $reason
        HeapSlope  = $heapSlope
        RssSlope   = $rssSlope
        HeapR2     = $heapR2
    }
}

$verdict = Get-LeakVerdict -Findings $findings

# endregion

# region: Формирование отчёта

$timestamp = (Get-Date).ToString("yyyy-MM-dd_HH-mm-ss")
$isoNow = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

$report = [pscustomobject]@{
    generatedAt   = $isoNow
    prometheusUrl = $PrometheusUrl
    windowHours   = $Hours
    step          = $Step
    job           = $Job
    verdict       = $verdict
    findings      = $findings
}

# --- Markdown ---
$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine("# Отчёт по утечке памяти — ArtasWriter")
[void]$md.AppendLine("")
[void]$md.AppendLine("- **Сформирован:** $isoNow")
[void]$md.AppendLine("- **Источник:** $PrometheusUrl")
[void]$md.AppendLine("- **Окно анализа:** последние $Hours ч (step=$Step)")
[void]$md.AppendLine("- **Фильтр:** ``$Job``")
[void]$md.AppendLine("")

$verdictIcon = switch ($verdict.Level) {
    "CRITICAL" { "🔴 CRITICAL" }
    "WARNING"  { "🟡 WARNING" }
    default    { "🟢 OK" }
}
[void]$md.AppendLine("## Итог: $verdictIcon")
[void]$md.AppendLine("")
[void]$md.AppendLine("$($verdict.Reason)")
[void]$md.AppendLine("")
[void]$md.AppendLine("| Показатель | Значение |")
[void]$md.AppendLine("|------------|----------|")
[void]$md.AppendLine("| Рост managed heap | {0:N1} МБ/час (R²={1:N2}) |" -f $verdict.HeapSlope, $verdict.HeapR2)
[void]$md.AppendLine("| Рост RSS (working set) | {0:N1} МБ/час |" -f $verdict.RssSlope)
[void]$md.AppendLine("")

if ($findings.Count -gt 0) {
    [void]$md.AppendLine("## Детализация метрик")
    [void]$md.AppendLine("")
    [void]$md.AppendLine("| Метрика | Поколение | Начало | Конец | Min | Max | Тренд | R² | Точек |")
    [void]$md.AppendLine("|---------|-----------|--------|-------|-----|-----|-------|----|-------|")
    foreach ($f in $findings) {
        $unit = if ($f.Unit -eq "MB") { "MB" } else { $f.Unit }
        $row = "| {0} | {1} | {2:N1} {9} | {3:N1} {9} | {4:N1} {9} | {5:N1} {9} | {6:N2} {9}/ч | {7:N2} | {8} |" -f `
            $f.Description, $f.Generation, $f.First, $f.Last, $f.Min, $f.Max, $f.SlopeMbPerHr, $f.R2, $f.Samples, $unit
        [void]$md.AppendLine($row)
    }
    [void]$md.AppendLine("")
} else {
    [void]$md.AppendLine("## Детализация метрик")
    [void]$md.AppendLine("")
    [void]$md.AppendLine("_Метрики не найдены. Убедитесь, что сервис запущен и отправляет runtime-метрики в Prometheus._")
    [void]$md.AppendLine("")
}

[void]$md.AppendLine("---")
[void]$md.AppendLine("")
[void]$md.AppendLine("## Как интерпретировать")
[void]$md.AppendLine("")
[void]$md.AppendLine("- **managed heap (MB/ч)** — устойчивый рост при стабильной нагрузке = кандидат на утечку managed-объектов.")
[void]$md.AppendLine("- **RSS / working set** — рост может отражать как managed, так и native-утечки.")
[void]$md.AppendLine("- **R²** — качество линейного фита; < 0.5 означает шумные/недостаточные данные.")
[void]$md.AppendLine("- **Gen2 collections** — частые сборки без снижения heap указывают на долгоживущие объекты.")
[void]$md.AppendLine("")
[void]$md.AppendLine("_Проверить полный список метрик: $PrometheusUrl/api/v1/label/__name__/values_")

$mdContent = $md.ToString()

# endregion

# region: Запись файлов

$written = @()

if ($Format -eq "Markdown" -or $Format -eq "Both") {
    $mdPath = Join-Path $OutputDir "memory_leak_report_$timestamp.md"
    [System.IO.File]::WriteAllText($mdPath, $mdContent, [System.Text.UTF8Encoding]::new($false))
    $written += $mdPath
}

if ($Format -eq "Json" -or $Format -eq "Both") {
    $jsonPath = Join-Path $OutputDir "memory_leak_report_$timestamp.json"
    $json = $report | ConvertTo-Json -Depth 6
    [System.IO.File]::WriteAllText($jsonPath, $json, [System.Text.UTF8Encoding]::new($false))
    $written += $jsonPath
}

# endregion

Write-Host ""
Write-Host "=== Итог: $($verdict.Level) ===" -ForegroundColor $(switch ($verdict.Level) { "CRITICAL" {"Red"} "WARNING" {"Yellow"} default {"Green"} })
Write-Host $verdict.Reason
Write-Host ("Managed heap: {0:N1} МБ/час, RSS: {1:N1} МБ/час" -f $verdict.HeapSlope, $verdict.RssSlope)
Write-Host ""
foreach ($p in $written) {
    Write-Ok "Отчёт: $p"
}

if ($written.Count -eq 0) {
    Write-Err "Ни один отчёт не записан."
    exit 1
}
