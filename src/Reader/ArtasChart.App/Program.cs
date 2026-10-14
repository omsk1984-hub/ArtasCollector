using System;
using System.Threading.Tasks;
using Avalonia;
using ArtasChart.App.ViewModels;
using ArtasCore.Abstractions;
using ArtasCore.Data;
using ArtasChart.Data;
using ArtasChart.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArtasChart.App;

internal static class Program
{
    /// <summary>
    /// Контейнер зависимостей приложения (композиция — в <see cref="RegisterServices"/>).
    /// Заменяет глобальный статик конфигурации: всё разрешается через DI.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    [STAThread]
    public static void Main(string[] args)
    {
        // Кириллица в консоли: дочерний .NET-процесс стартует с
        // Console.OutputEncoding = коду консоли (CP866), из-за чего русский
        // текст в CLI-выводе ломается («кракозябры»). UTF-8-префикс у
        // родительского pwsh на дочерний процесс не влияет — лечим внутри C#,
        // до первого Console.WriteLine.
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.InputEncoding = System.Text.Encoding.UTF8;
        }
        catch
        {
            // Не критично: если консоли нет (GUI-запуск), просто продолжаем.
        }

        // Глобальный перехват: WinExe не пишет исключения в консоль по умолчанию,
        // поэтому без этого ошибки старта выглядели бы как «зависание».
        try
        {
            var configuration = BuildConfiguration();
            Console.WriteLine("[start] Конфигурация загружена.");
            Services = RegisterServices(configuration);
            Console.WriteLine("[start] Сервисы зарегистрированы.");

            // CLI-режим проверки данных без запуска GUI: ArtasChart.App --check-db [symbol]
            if (Array.Exists(args, a => a == "--check-db"))
            {
                var symbol = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : "Si";
                Services.GetRequiredService<IDiagnosticsService>()
                    .CheckDbAsync(symbol)
                    .GetAwaiter()
                    .GetResult();
                return;
            }

            Console.WriteLine("[start] Запуск Avalonia UI...");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[fatal] Необработанное исключение при запуске:");
            Console.Error.WriteLine(ex.ToString());
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Сборка конфигурации: базовая (appsettings.json) + переменные окружения
    /// (последние имеют приоритет).
    /// </summary>
    private static IConfiguration BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();
    }

    /// <summary>
    /// Композиция зависимостей приложения (инверсия управления + внедрение зависимостей).
    /// </summary>
    private static IServiceProvider RegisterServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();

        // Конфигурация как сервис (вместо глобального статика).
        services.AddSingleton(configuration);
        services.AddSingleton(DbConfig.FromConfiguration(configuration));

        // Data-слой: единый репозиторий, зарегистрированный под сегрегированными интерфейсами.
        services.AddSingleton<FractalRepository>();
        services.AddSingleton<IFractalReader>(sp => sp.GetRequiredService<FractalRepository>());
        services.AddSingleton<IHealthProbe>(sp => sp.GetRequiredService<FractalRepository>());
        services.AddSingleton<IFractalCounter>(sp => sp.GetRequiredService<FractalRepository>());

        // Хранилище настроек фильтра по символам (отдельная таблица symbol_filter_settings).
        services.AddSingleton<ISymbolFilterSettingsStore, SymbolFilterSettingsRepository>();

        // Services-слой.
        services.AddSingleton<IFractalFilter, FractalFilter>();
        services.AddSingleton<ILocalExtremumFilter, LocalExtremumFilter>();
        services.AddSingleton<IFractalSeriesPreparer, FractalSeriesPreparer>();
        services.AddSingleton<IFractalInfoFormatter, FractalInfoFormatter>();
        services.AddSingleton<IDiagnosticsService, DiagnosticsService>();

        // Слой презентации: подготовка серии и периодический опрос.
        services.AddSingleton<IChartSeriesBuilder, ChartSeriesBuilder>();
        services.AddSingleton<IPollingService, PollingService>();

        // Слой представления: ViewModel главного окна.
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
