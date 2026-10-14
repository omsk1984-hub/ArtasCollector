using System.Globalization;
using System.Text;
using System.Text.Json;
using ArtasTools.ReportAnalyzer.Models;

namespace ArtasTools.ReportAnalyzer.Reporting;

/// <summary>Рендеринг сводного отчёта анализа в Markdown и JSON.</summary>
public static class ReportRenderer
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string ToJson(AnalysisResult result) => JsonSerializer.Serialize(result, JsonOpts);

    public static string ToMarkdown(AnalysisResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Сводный анализ отчётов по памяти — ArtasWriter");
        sb.AppendLine();
        sb.AppendLine($"- **Сформирован:** {result.GeneratedAt}");
        sb.AppendLine($"- **Каталог отчётов:** `{result.ReportsDir}`");
        sb.AppendLine($"- **Проанализировано отчётов:** {result.ReportsAnalyzed}");
        sb.AppendLine();

        var icon = result.VerdictEnum switch
        {
            IssueSeverity.Critical => "🔴 CRITICAL",
            IssueSeverity.Warning => "🟡 WARNING",
            _ => "🟢 OK"
        };
        sb.AppendLine($"## Итог: {icon}");
        sb.AppendLine();
        sb.AppendLine(result.Reason);
        sb.AppendLine();

        // Сводная таблица по отчётам.
        if (result.Reports.Count > 0)
        {
            sb.AppendLine("## Отчёты");
            sb.AppendLine();
            sb.AppendLine("| Отчёт | Сформирован | Фильтр | Вердикт | Heap, МБ/ч | RSS, МБ/ч | R² | Проблем |");
            sb.AppendLine("|-------|-------------|--------|---------|-----------|----------|----|---------|");
            foreach (var r in result.Reports)
            {
                var vIcon = r.VerdictLevel.ToUpperInvariant() switch
                {
                    "CRITICAL" => "🔴 CRITICAL",
                    "WARNING" => "🟡 WARNING",
                    _ => "🟢 OK"
                };
                var problems = r.Issues.Count(i => i.SeverityEnum != IssueSeverity.Info);
                sb.AppendLine($"| `{r.File}` | {r.GeneratedAt} | `{r.Job}` | {vIcon} | " +
                              $"{N(r.HeapSlopeMbPerHour)} | {N(r.RssSlopeMbPerHour)} | {N2(r.HeapR2)} | {problems} |");
            }
            sb.AppendLine();
        }

        // Агрегированные проблемы.
        sb.AppendLine("## Выявленные проблемы");
        sb.AppendLine();
        if (result.TopIssues.Count == 0)
        {
            sb.AppendLine("_Проблем не обнаружено._");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("| Серьёзность | Категория | Метрика | Наклон | R² | Источник | Описание |");
            sb.AppendLine("|-------------|-----------|---------|--------|----|----------|----------|");
            foreach (var i in result.TopIssues)
            {
                var sIcon = i.SeverityEnum switch
                {
                    IssueSeverity.Critical => "🔴 CRITICAL",
                    IssueSeverity.Warning => "🟡 WARNING",
                    _ => "🔵 INFO"
                };
                sb.AppendLine($"| {sIcon} | {i.Kind} | {i.Metric} | {N2(i.SlopePerHour)} | {N2(i.R2)} | " +
                              $"{Source(i)} | {Escape(i.Message)} |");
            }
            sb.AppendLine();
            sb.AppendLine("_Источник указывает файл и дату отчёта, из которого получена проблема: " +
                          "сигнал из старого отчёта может относиться к уже перезапущенному инстансу._");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Как интерпретировать");
        sb.AppendLine();
        sb.AppendLine("- **CRITICAL** — устойчивый рост heap/RSS выше критичного порога при достоверном фите (R² ≥ 0.5).");
        sb.AppendLine("- **WARNING** — заметный устойчивый рост или удержание heap у максимума (долгоживущие объекты).");
        sb.AppendLine("- **INFO** — слабый/шумный тренд либо рост допустимой метрики — на наблюдение.");
        sb.AppendLine("- **DataQuality** — недостаток точек или шумный фит: тренд нельзя считать надёжным.");
        sb.AppendLine();
        sb.AppendLine($"_Источник: JSON-отчёты ArtasTools.MemReport в `{result.ReportsDir}`._");

        return sb.ToString();
    }

    private static string N(double v) => Safe(v).ToString("N1", CultureInfo.CurrentCulture);
    private static string N2(double v) => Safe(v).ToString("N2", CultureInfo.CurrentCulture);
    private static double Safe(double v) => double.IsFinite(v) ? v : 0.0;

    /// <summary>Метка источника проблемы: файл и дата отчёта (для Markdown-таблиц).</summary>
    private static string Source(AnalysisIssue i)
    {
        if (string.IsNullOrEmpty(i.SourceFile) && string.IsNullOrEmpty(i.GeneratedAt))
            return "—";
        var at = string.IsNullOrEmpty(i.GeneratedAt) ? "" : $" ({i.GeneratedAt})";
        return Escape($"{i.SourceFile}{at}");
    }

    /// <summary>Экранирование вертикальной черты/переводов строк для Markdown-таблиц.</summary>
    private static string Escape(string text) =>
        text.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
