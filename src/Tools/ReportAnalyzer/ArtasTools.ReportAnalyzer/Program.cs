using System.Text;
using ArtasTools.ReportAnalyzer.Analysis;
using ArtasTools.ReportAnalyzer.Models;
using ArtasTools.ReportAnalyzer.Options;
using ArtasTools.ReportAnalyzer.Reporting;

namespace ArtasTools.ReportAnalyzer;

/// <summary>
/// Точка входа консоли анализа JSON-отчётов ArtasTools.MemReport.
/// Читает каталог отчётов, оценивает тренды памяти и формирует сводку.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Консольный вывод в UTF-8: иначе русский текст ломается ("кракозябры"),
        // т.к. процесс стартует с Console.OutputEncoding = коду консоли (CP866).
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // нет консоли (перенаправление/отвязанный процесс) — не критично
        }

        if (args.Contains("-h") || args.Contains("--help"))
        {
            PrintHelp();
            return 0;
        }

        var options = ParseArgs(args);
        var reportsDir = string.IsNullOrWhiteSpace(options.ReportsDir)
            ? Path.Combine(FindProjectRoot(), "reports")
            : options.ReportsDir;

        Console.WriteLine($"[REPORT-ANALYZER] Каталог отчётов: {reportsDir}");
        if (options.LastHours > 0)
            Console.WriteLine($"[REPORT-ANALYZER] Окно: только отчёты за последние {options.LastHours} ч");

        var engine = new AnalysisEngine(options);
        var result = await engine.AnalyzeAsync(reportsDir);

        if (result.ReportsAnalyzed == 0)
        {
            Console.Error.WriteLine($"[WARN] В каталоге нет отчётов memory_leak_report_*.json.");
            Console.WriteLine("[INFO] Сначала сформируйте отчёт: .\\src\\Tools\\MemReport\\run_MemReport.ps1");
        }

        var written = WriteOutputs(result, options);

        Console.WriteLine();
        var color = result.VerdictEnum switch
        {
            IssueSeverity.Critical => ConsoleColor.Red,
            IssueSeverity.Warning => ConsoleColor.Yellow,
            _ => ConsoleColor.Green
        };
        Console.ForegroundColor = color;
        Console.WriteLine($"=== Итог: {result.Verdict} ===");
        Console.ResetColor();
        Console.WriteLine(result.Reason);
        Console.WriteLine($"Проанализировано отчётов: {result.ReportsAnalyzed}, " +
                          $"проблем в сводке: {result.TopIssues.Count}");
        Console.WriteLine();

        foreach (var path in written)
            Console.WriteLine($"[OK] Отчёт: {path}");

        if (written.Count == 0)
        {
            Console.Error.WriteLine("[ERROR] Ни один отчёт не записан.");
            return 1;
        }

        // Код возврата 2 при критичных проблемах — удобно для CI.
        return result.VerdictEnum == IssueSeverity.Critical ? 2 : 0;
    }

    private static List<string> WriteOutputs(AnalysisResult result, AnalyzerOptions options)
    {
        var outDir = string.IsNullOrWhiteSpace(options.OutputDir)
            ? Path.Combine(FindProjectRoot(), "reports")
            : options.OutputDir!;

        Directory.CreateDirectory(outDir);
        var written = new List<string>();
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var utf8 = new System.Text.UTF8Encoding(false);

        if (options.Format is OutputFormat.Markdown or OutputFormat.Both)
        {
            var mdPath = Path.Combine(outDir, $"memory_analysis_{stamp}.md");
            File.WriteAllText(mdPath, ReportRenderer.ToMarkdown(result), utf8);
            written.Add(mdPath);
        }

        if (options.Format is OutputFormat.Json or OutputFormat.Both)
        {
            var jsonPath = Path.Combine(outDir, $"memory_analysis_{stamp}.json");
            File.WriteAllText(jsonPath, ReportRenderer.ToJson(result), utf8);
            written.Add(jsonPath);
        }

        if (options.PrintToConsole)
        {
            Console.WriteLine();
            Console.WriteLine(ReportRenderer.ToMarkdown(result));
        }

        return written;
    }

    /// <summary>
    /// Корень репозитория: поднимаемся от каталога сборки до папки с .git.
    /// Fallback — шесть уровней вверх (bin/Debug/net10.0 → проект → ReportAnalyzer → Tools → src → корень).
    /// </summary>
    private static string FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
    }

    private static AnalyzerOptions ParseArgs(string[] args)
    {
        var o = new AnalyzerOptions();
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : "";

            switch (a)
            {
                case "--reports-dir": o.ReportsDir = Next(); break;
                case "--output-dir": o.OutputDir = Next(); break;
                case "--last-hours": if (int.TryParse(Next(), out var h)) o.LastHours = h; break;
                case "--min-r2": if (double.TryParse(Next(), out var r2)) o.MinR2 = r2; break;
                case "--format":
                    var fmt = Next();
                    o.Format = fmt.Equals("json", StringComparison.OrdinalIgnoreCase) ? OutputFormat.Json
                             : fmt.Equals("markdown", StringComparison.OrdinalIgnoreCase) ? OutputFormat.Markdown
                             : OutputFormat.Both;
                    break;
                case "--print": o.PrintToConsole = true; break;
                default:
                    if (a.StartsWith("--")) Console.Error.WriteLine($"[WARN] Неизвестный аргумент: {a}");
                    break;
            }
        }
        return o;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            ArtasTools.ReportAnalyzer — анализ JSON-отчётов ArtasTools.MemReport.

            Использование:
              dotnet run --project src/Tools/ReportAnalyzer/ArtasTools.ReportAnalyzer [опции]

            Опции:
              --reports-dir <path>   Каталог с отчётами (по умолчанию <корень>/reports)
              --output-dir <path>    Каталог сводных отчётов (по умолчанию <корень>/reports)
              --last-hours <n>       Анализировать только отчёты за последние n часов (0 — все)
              --min-r2 <x>           Порог достоверности линейного фита (по умолчанию 0.5)
              --format <md|json|both> Форматы вывода (по умолчанию both)
              --print                Дополнительно вывести Markdown в консоль
              -h, --help             Показать справку

            Коды возврата:
              0 — успешно (критичных проблем нет)
              1 — ошибка (нет отчётов / не записан результат)
              2 — выявлены критичные проблемы (уровень CRITICAL)
            """);
    }
}
