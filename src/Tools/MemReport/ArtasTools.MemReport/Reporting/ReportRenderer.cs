using System.Globalization;
using System.Text;
using System.Text.Json;
using ArtasTools.MemReport.Models;

namespace ArtasTools.MemReport.Reporting;

/// <summary>Рендеринг отчёта в Markdown.</summary>
public static class ReportRenderer
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string ToJson(MemoryReport report) => JsonSerializer.Serialize(report, JsonOpts);

    public static string ToMarkdown(MemoryReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Отчёт по утечке памяти — ArtasWriter");
        sb.AppendLine();
        sb.AppendLine($"- **Сформирован:** {report.GeneratedAt}");
        sb.AppendLine($"- **Источник:** {report.PrometheusUrl}");
        sb.AppendLine($"- **Окно анализа:** последние {report.WindowHours} ч (step={report.Step})");
        sb.AppendLine($"- **Фильтр:** `{report.Job}`");
        sb.AppendLine();

        var icon = report.Verdict.LevelEnum switch
        {
            LeakLevel.Critical => "🔴 CRITICAL",
            LeakLevel.Warning => "🟡 WARNING",
            _ => "🟢 OK"
        };

        sb.AppendLine($"## Итог: {icon}");
        sb.AppendLine();
        sb.AppendLine(report.Verdict.Reason);
        sb.AppendLine();
        sb.AppendLine("| Показатель | Значение |");
        sb.AppendLine("|------------|----------|");
        sb.AppendLine($"| Рост managed heap | {Fmt(report.Verdict.HeapSlopeMbPerHour)} МБ/час (R²={Fmt(report.Verdict.HeapR2)}) |");
        sb.AppendLine($"| Рост RSS (working set) | {Fmt(report.Verdict.RssSlopeMbPerHour)} МБ/час |");
        sb.AppendLine();

        if (report.Findings.Count > 0)
        {
            // Показываем колонку «Инстанс», если (при пустом фильтре) в отчёте
            // присутствуют серии от нескольких процессов.
            var multiInstance = report.Findings
                .Where(f => !string.IsNullOrEmpty(f.Instance))
                .Select(f => f.Instance)
                .Distinct()
                .Count() > 1;

            sb.AppendLine("## Детализация метрик");
            sb.AppendLine();
            sb.AppendLine(multiInstance
                ? "| Метрика | Инстанс | Поколение | Начало | Конец | Min | Max | Тренд | Post-GC тренд | R² | Точек | Длит. |"
                : "| Метрика | Поколение | Начало | Конец | Min | Max | Тренд | Post-GC тренд | R² | Точек | Длит. |");
            sb.AppendLine(multiInstance
                ? "|---------|---------|-----------|--------|-------|-----|-----|-------|---------------|----|-------|-------|"
                : "|---------|-----------|--------|-------|-----|-----|-------|---------------|----|-------|-------|");

            // Отмечаем недостоверные серии (мало точек/короткое окно): именно они
            // ранее давали ложные CRITICAL.
            var hasUnreliable = report.Findings.Any(f => !f.Reliable);

            foreach (var f in report.Findings)
            {
                var instCol = multiInstance ? $" {f.Instance} |" : "";
                // Байтовые метрики отдаются в МБ (нормализация сделана при построении),
                // числовые (count/rate) — в собственных единицах.
                var unit = f.IsBytes ? "MB" : f.Unit;
                double Sl(double v) => double.IsFinite(v) ? v : 0.0;
                var mark = f.Reliable ? "" : " ⚠";
                var durationMin = f.DurationSeconds / 60;

                sb.AppendLine($"| {f.Description}{mark} |{instCol} {f.Generation} | {Sl(f.First):N1} {unit} | " +
                              $"{Sl(f.Last):N1} {unit} | {Sl(f.Min):N1} {unit} | {Sl(f.Max):N1} {unit} | " +
                              $"{Sl(f.SlopePerHour):N2} {unit}/ч | {Sl(f.BaselineSlopePerHour):N2} {unit}/ч | " +
                              $"{Sl(f.R2):N2} | {f.Samples} | {durationMin} мин |");
            }
            sb.AppendLine();
            if (hasUnreliable)
            {
                sb.AppendLine("_⚠ — серия помечена как недостоверная (мало точек или короткое окно) " +
                              "и не участвует в итоговом вердикте._");
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("## Детализация метрик");
            sb.AppendLine();
            sb.AppendLine("_Метрики не найдены. Убедитесь, что сервис запущен и отправляет runtime-метрики в Prometheus._");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Как интерпретировать");
        sb.AppendLine();
        sb.AppendLine("- **managed heap (MB/ч)** — устойчивый рост при стабильной нагрузке = кандидат на утечку managed-объектов.");
        sb.AppendLine("- **Post-GC тренд (MB/ч)** — тренд нижней огибающей (минимумы после сборок). Растёт — heap не " +
                      "возвращается к прежней базовой линии: сильный признак долгоживущих (Gen2) объектов. " +
                      "Средний тренд «пилообразного» heap может быть верхнеграничным шумом.");
        sb.AppendLine("- **RSS / working set** — рост может отражать как managed, так и native-утечки.");
        sb.AppendLine("- **R²** — качество линейного фита; < 0.5 означает шумные/недостаточные данные.");
        sb.AppendLine("- **Gen2/LOH heap size** — рост heap поколений 2/LOH подтверждает утечку долгоживущих объектов.");
        sb.AppendLine("- **Достоверность** — вердикт строится только по сериям с достаточным числом точек и длиной окна; " +
                      "короткие стартовые серии помечены ⚠ и игнорируются.");
        sb.AppendLine();
        sb.AppendLine($"_Проверить полный список метрик: {report.PrometheusUrl}/api/v1/label/__name__/values_");

        return sb.ToString();
    }

    /// <summary>Форматирование числа с защитой от NaN/Infinity.</summary>
    private static string Fmt(double v) => (double.IsFinite(v) ? v : 0.0).ToString("N1", CultureInfo.CurrentCulture);
}
