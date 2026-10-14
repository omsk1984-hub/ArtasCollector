using System.Text;
using ArtasTools.MemReport.Models;
using ArtasTools.MemReport.Options;
using ArtasTools.MemReport.Reporting;
using ArtasTools.MemReport.Services;

namespace ArtasTools.MemReport;

/// <summary>
/// Точка входа консоли формирования отчёта по утечке памяти из Prometheus.
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
        Console.WriteLine($"[MEM-REPORT] Prometheus: {options.PrometheusUrl}");
        Console.WriteLine($"[MEM-REPORT] Окно анализа: последние {options.Hours} ч (step={options.Step})");

        using var client = new PrometheusClient(options.PrometheusUrl);

        if (!await client.IsAvailableAsync())
        {
            Console.Error.WriteLine($"[ERROR] Prometheus недоступен по адресу {options.PrometheusUrl}");
            Console.WriteLine("[INFO] Запустите стек мониторинга: .\\scripts\\manage_monitoring.ps1 -Action start");
            return 1;
        }
        Console.WriteLine("[OK] Prometheus доступен.");

        var builder = new MemoryReportBuilder(client);
        var report = await builder.BuildAsync(options);

        if (report.Findings.Count == 0)
        {
            Console.WriteLine($"[WARN] Не найдено ни одной метрики памяти по фильтру '{options.Job}'.");
            Console.WriteLine("[INFO] Проверьте список метрик:");
            Console.WriteLine($"  {options.PrometheusUrl}/api/v1/label/__name__/values");
            var names = await client.GetMetricNamesAsync();
            var dbNames = names.Where(n => n.Contains("gc") || n.Contains("memory") || n.Contains("dotnet"))
                               .OrderBy(n => n).ToList();
            if (dbNames.Count > 0)
            {
                Console.WriteLine($"[INFO] Доступные dotnet/gc-метрики ({dbNames.Count}):");
                foreach (var n in dbNames) Console.WriteLine($"  - {n}");
            }
        }

        var written = WriteOutputs(report, options);

        Console.WriteLine();
        var color = report.Verdict.LevelEnum switch
        {
            LeakLevel.Critical => ConsoleColor.Red,
            LeakLevel.Warning => ConsoleColor.Yellow,
            _ => ConsoleColor.Green
        };
        Console.ForegroundColor = color;
        Console.WriteLine($"=== Итог: {report.Verdict.Level} ===");
        Console.ResetColor();
        Console.WriteLine(report.Verdict.Reason);
        Console.WriteLine($"Managed heap: {report.Verdict.HeapSlopeMbPerHour:N1} МБ/час, " +
                          $"RSS: {report.Verdict.RssSlopeMbPerHour:N1} МБ/час");
        Console.WriteLine();

        foreach (var path in written)
            Console.WriteLine($"[OK] Отчёт: {path}");

        if (written.Count == 0)
        {
            Console.Error.WriteLine("[ERROR] Ни один отчёт не записан.");
            return 1;
        }

        // Код возврата 2 при критичном вердикте — удобно для CI.
        return report.Verdict.LevelEnum == LeakLevel.Critical ? 2 : 0;
    }

    private static List<string> WriteOutputs(MemoryReport report, ReportOptions options)
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
            var mdPath = Path.Combine(outDir, $"memory_leak_report_{stamp}.md");
            File.WriteAllText(mdPath, ReportRenderer.ToMarkdown(report), utf8);
            written.Add(mdPath);
        }

        if (options.Format is OutputFormat.Json or OutputFormat.Both)
        {
            var jsonPath = Path.Combine(outDir, $"memory_leak_report_{stamp}.json");
            File.WriteAllText(jsonPath, ReportRenderer.ToJson(report), utf8);
            written.Add(jsonPath);
        }

        if (options.PrintToConsole)
        {
            Console.WriteLine();
            Console.WriteLine(ReportRenderer.ToMarkdown(report));
        }

        return written;
    }

    /// <summary>
    /// Корень репозитория: поднимаемся от каталога сборки до папки с каталогом
    /// .git. Fallback — пять уровней вверх (bin/Debug/net10.0 → проект → Tools → src → корень).
    /// </summary>
    private static string FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        // Fallback: относительный путь от каталога сборки вверх.
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static ReportOptions ParseArgs(string[] args)
    {
        var o = new ReportOptions();
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : "";

            switch (a)
            {
                case "--prometheus-url": o.PrometheusUrl = Next(); break;
                case "--hours": if (int.TryParse(Next(), out var h)) o.Hours = h; break;
                case "--step": o.Step = Next(); break;
                case "--output-dir": o.OutputDir = Next(); break;
                case "--job": o.Job = Next(); break;
                case "--min-samples": if (int.TryParse(Next(), out var ms)) o.MinSamples = ms; break;
                case "--min-duration-minutes": if (int.TryParse(Next(), out var md)) o.MinDurationMinutes = md; break;
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
            ArtasTools.MemReport — отчёт по утечке памяти из Prometheus.

            Использование:
              dotnet run --project src/Tools/ArtasTools.MemReport [опции]

            Опции:
              --prometheus-url <url>   Базовый URL Prometheus (по умолчанию http://localhost:9090)
              --hours <n>              Глубина окна анализа в часах (по умолчанию 12)
              --step <duration>        Шаг выборки, напр. 60s (по умолчанию 60s)
              --output-dir <path>      Каталог отчётов (по умолчанию <корень>/reports)
              --job <service_name>     Фильтр по метке service_name (по умолчанию artas-writer)
              --min-samples <n>        Мин. число точек серии для достоверного тренда (по умолчанию 30)
              --min-duration-minutes <n>  Мин. длительность окна серии, мин (по умолчанию 60)
              --format <md|json|both>  Форматы вывода (по умолчанию both)
              --print                  Дополнительно вывести Markdown в консоль
              -h, --help               Показать справку

            Коды возврата:
              0 — успешно (утечка не выявлена)
              1 — ошибка (Prometheus недоступен / не записан отчёт)
              2 — выявлена критичная утечка (уровень CRITICAL)
            """);
    }
}
