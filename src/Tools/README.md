# ArtasTools — инструменты диагностики Artas2026

Набор вспомогательных консольных утилит для анализа эксплуатационных данных
проекта.

## Структура

```
src/Tools/
├── ArtasTools.slnx                  # единое решение (оба инструмента)
├── Directory.Build.props            # общие свойства сборки (net10.0)
├── run_MemDiagnostics.ps1           # общий запуск: MemReport → ReportAnalyzer
├── MemReport/
│   ├── ArtasTools.MemReport/        # формирование отчёта по памяти из Prometheus
│   ├── ArtasTools.MemReport.Tests/  # тесты MemReport
│   └── run_MemReport.ps1            # обёртка запуска MemReport
└── ReportAnalyzer/
    ├── ArtasTools.ReportAnalyzer/       # анализ готовых JSON-отчётов
    ├── ArtasTools.ReportAnalyzer.Tests/ # тесты ReportAnalyzer
    └── run_ReportAnalyzer.ps1           # обёртка запуска ReportAnalyzer
```

## ArtasTools.MemReport

Консоль (.NET 10) для формирования отчёта о потреблении памяти сервисом
**ArtasWriter** на основе runtime-метрик .NET, которые OTel Collector пишет в
Prometheus (remote-write). Инструмент считает линейный тренд метрик памяти,
оценивает вероятность утечки и сохраняет отчёт в форматах Markdown и JSON.

### Как это работает

```
ArtasWriter (.NET runtime instrumentation)
      │ OTLP (gRPC :4317)
      ▼
OTel Collector ──prometheusremotewrite──▶ Prometheus (:9090, TSDB)
                                                ▲
                          ArtasTools.MemReport ─┘ HTTP API /api/v1/query_range
                                                │
                                                ▼
                                   reports/memory_leak_report_*.md|.json
```

Инструмент опрашивает HTTP API Prometheus, считает по каждой метрике
линейную регрессию (наклон в МБ/час и R²) и формирует итоговую оценку:

| Уровень | Условие |
|---------|---------|
| 🟢 OK | нет устойчивого роста |
| 🟡 WARNING | heap > 10 МБ/час, RSS > 20 МБ/час или Gen2/LOH > 5 МБ/час |
| 🔴 CRITICAL | heap > 50 МБ/час, RSS > 100 МБ/час или Gen2/LOH > 40 МБ/час |

> **Важно — борьба с ложными срабатываниями.** Вердикт строится по
> наихудшему (максимальному) наклону среди **достоверных** серий одного
> инстанса, а не как сумма наклонов всех серий. Серия считается достоверной,
> если набрано достаточно точек (`--min-samples`) и достаточная длительность
> окна (`--min-duration-minutes`): короткие стартовые серии перезапущенного
> процесса (7–8 точек прогрев) исключаются. Дополнительно считается тренд
> **нижней огибающей** (post-GC минимумы): его устойчивый рост означает, что
> после сборок heap не возвращается к прежней базовой линии — признак
> долгоживущих (Gen2) объектов.

### Анализируемые метрики

| Ключ | Метрика Prometheus | Описание |
|------|--------------------|----------|
| managed_heap | `dotnet_gc_last_collection_heap_size_bytes` | Размер managed heap |
| rss | `dotnet_process_memory_working_set_bytes` | Working set процесса (RSS) |
| gc_committed | `dotnet_gc_last_collection_memory_committed_size_bytes` | Закоммиченная память heap |
| gc_fragmentation | `dotnet_gc_last_collection_heap_fragmentation_size_bytes` | Фрагментация heap |
| heap_gen | `dotnet_gc_heap_size_bytes` | Размер heap по поколениям (Gen0/1/2/LOH) |
| alloc_rate | `rate(dotnet_gc_heap_allocated_bytes_total[...])` | Скорость аллокаций |
| gc_collections | `rate(dotnet_gc_collections_total[...])` | Сборки GC по поколениям |
| gc_pause | `rate(dotnet_gc_pause_time_seconds_total[...])` | Доля времени в паузах GC |
| threads | `dotnet_thread_pool_thread_count_total` | Потоки пула |

> Имена метрик соответствуют фактической runtime-инструментации .NET 10.
> Полный список: `http://localhost:9090/api/v1/label/__name__/values`.

### Запуск

```powershell
# Через скрипт-обёртку (рекомендуется)
.\src\Tools\MemReport\run_MemReport.ps1
.\src\Tools\MemReport\run_MemReport.ps1 -Hours 24 -Job ""
.\src\Tools\MemReport\run_MemReport.ps1 -Print

# Напрямую через dotnet
dotnet run --project src/Tools/MemReport/ArtasTools.MemReport -- --hours 6
```

### Опции

| Опция | По умолчанию | Описание |
|-------|--------------|----------|
| `--prometheus-url` | `http://localhost:9090` | Базовый URL Prometheus API |
| `--hours` | `12` | Глубина окна анализа, ч |
| `--step` | `60s` | Шаг выборки range-запросов |
| `--output-dir` | `<корень>/reports` | Каталог отчётов |
| `--job` | `artas-writer` | Фильтр по метке `service_name` (пусто — все) |
| `--min-samples` | `30` | Мин. число точек серии для достоверного тренда |
| `--min-duration-minutes` | `60` | Мин. длительность окна серии, мин |
| `--format` | `both` | `md` / `json` / `both` |
| `--print` | — | Дополнительно вывести Markdown в консоль |
| `-h`, `--help` | — | Справка |

### Коды возврата

| Код | Значение |
|-----|----------|
| 0 | Успешно (утечка не выявлена) |
| 1 | Ошибка (Prometheus недоступен / отчёт не записан) |
| 2 | Выявлена критичная утечка (уровень CRITICAL) — удобно для CI |

## ArtasTools.ReportAnalyzer

Консоль (.NET 10) для **анализа уже сформированных** JSON-отчётов MemReport из
каталога `reports/`. В отличие от MemReport, не опрашивает Prometheus, а работает
офлайн — что позволяет анализировать накопленную историю и сравнивать несколько
отчётов между собой.

### Как это работает

```
reports/memory_leak_report_*.json
        │
        ▼
ArtasTools.ReportAnalyzer
   ├─ читает отчёты (окно --last-hours)
   ├─ оценивает тренды по порогам и достоверности (R²)
   ├─ формирует список проблем (issues)
   └─▶ reports/memory_analysis_*.md|.json
```

### Правила анализа

Инструмент не дублирует вердикт MemReport, а дополняет его детекцией:

| Категория | Условие |
|-----------|---------|
| `Gen2Pressure` | устойчивый рост heap **или** рост Gen2/LOH heap, **или** удержание heap у максимума (last ≥ 95% max) |
| `LeakTrend` | устойчивый рост RSS выше порогов |
| `Fragmentation` | устойчивый рост фрагментации heap |
| `WatchTrend` | слабый устойчивый рост — на наблюдение |
| `UnexpectedGrowth` | аномальный рост метрики, которая расти не должна |
| `DataQuality` | шумный фит (R² ниже порога), мало точек/короткое окно или отсутствие серий |
| `Verdict` | проброс вердикта исходного отчёта (WARNING/CRITICAL) — **только если** он подтверждён достоверной растущей серией |

Пороги совпадают с MemReport (heap 10/50 МБ/ч, RSS 20/100 МБ/ч, Gen2/LOH 5/40 МБ/ч),
достоверность тренда определяется по `--min-r2` (по умолчанию 0.5), а также по
минимальному числу точек (`MinSamples`, 30) и длительности окна (`MinDurationMinutes`, 60 мин).
Серии, не прошедшие эти проверки, помечаются как `DataQuality` и **не эскалируются** —
именно они ранее давали ложные CRITICAL. Проброс вердикта исходного отчёта также
отменяется, если в отчёте нет ни одной достоверной растущей сигнальной серии.

### Запуск

```powershell
# Через скрипт-обёртку (рекомендуется)
.\src\Tools\ReportAnalyzer\run_ReportAnalyzer.ps1
.\src\Tools\ReportAnalyzer\run_ReportAnalyzer.ps1 -LastHours 24 -Print
.\src\Tools\ReportAnalyzer\run_ReportAnalyzer.ps1 -ReportsDir .\reports -NoBuild

# Напрямую через dotnet
dotnet run --project src/Tools/ReportAnalyzer/ArtasTools.ReportAnalyzer
```

### Опции

| Опция | По умолчанию | Описание |
|-------|--------------|----------|
| `--reports-dir` | `<корень>/reports` | Каталог с отчётами `memory_leak_report_*.json` |
| `--output-dir` | `<корень>/reports` | Каталог сводных отчётов |
| `--last-hours` | `0` | Анализировать только отчёты за последние n часов (0 — все) |
| `--min-r2` | `0.5` | Порог достоверности линейного фита |
| `--format` | `both` | `md` / `json` / `both` |
| `--print` | — | Дополнительно вывести Markdown в консоль |
| `-h`, `--help` | — | Справка |

### Коды возврата

| Код | Значение |
|-----|----------|
| 0 | Успешно (критичных проблем нет) |
| 1 | Ошибка (нет отчётов / результат не записан) |
| 2 | Выявлены критичные проблемы (уровень CRITICAL) — удобно для CI |

## Общий запуск (рекомендуется)

Скрипт [`run_MemDiagnostics.ps1`](run_MemDiagnostics.ps1) последовательно выполняет
обе стадии: сначала формирует отчёт MemReport, затем анализирует его через
ReportAnalyzer.

### Как это работает

```
run_MemDiagnostics.ps1
   ├─ [1/2] MemReport      → reports/memory_leak_report_*.json|.md
   └─ [2/2] ReportAnalyzer → reports/memory_analysis_*.json|.md
```

### Запуск

```powershell
# Полный цикл (окно 6ч, фильтр artas-writer)
.\src\Tools\run_MemDiagnostics.ps1

# Большее окно анализа и вывод обеих стадий в консоль
.\src\Tools\run_MemDiagnostics.ps1 -Hours 24 -Print

# Без фильтра (все сервисы)
.\src\Tools\run_MemDiagnostics.ps1 -Job ""

# Только анализ уже готовых отчётов
.\src\Tools\run_MemDiagnostics.ps1 -SkipMemReport -LastHours 48
```

### Опции

| Опция | По умолчанию | Описание |
|-------|--------------|----------|
| `-Hours` | `12` | Глубина окна MemReport, ч |
| `-Step` | `60s` | Шаг выборки range-запросов |
| `-Job` | `artas-writer` | Фильтр по `service_name` (пусто — все) |
| `-PrometheusUrl` | `http://localhost:9090` | URL Prometheus API |
| `-LastHours` | `0` | Анализ только отчётов за последние n часов |
| `-MinR2` | `0.5` | Порог достоверности фита в анализаторе |
| `-Print` | — | Вывести Markdown обеих стадий в консоль |
| `-NoBuild` | — | Запуск без пересборки |
| `-SkipMemReport` | — | Пропустить стадию сбора отчёта |
| `-SkipAnalyzer` | — | Пропустить стадию анализа |

### Коды возврата

| Код | Значение |
|-----|----------|
| 0 | Успешно (утечка/критичных проблем нет) |
| 1 | Ошибка на любой из стадий |
| 2 | Выявлена критичная проблема (CRITICAL) — удобно для CI |

## Тесты

```powershell
dotnet test src/Tools/ArtasTools.slnx
```

## Требования

- .NET 10 SDK.
- Запущенный стек мониторинга (только для MemReport):
  `.\scripts\manage_monitoring.ps1 -Action start`
- Запущенный сервис ArtasWriter, отправляющий OTLP-метрики в коллектор.

## Альтернатива: PowerShell-скрипт

Идентичный по логике отчёт MemReport доступен как отдельный скрипт без сборки:
[`scripts/memory_leak_report.ps1`](../../scripts/memory_leak_report.ps1).
