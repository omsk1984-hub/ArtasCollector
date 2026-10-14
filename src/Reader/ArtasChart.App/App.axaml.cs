using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ArtasChart.App.ViewModels;
using ArtasCore.Models;
using ArtasChart.Data;
using ArtasChart.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ArtasChart.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Разрешаем зависимости через DI-контейнер (композиция — в Program).
            var viewModel = Program.Services.GetRequiredService<MainWindowViewModel>();

            // Символ по умолчанию можно задать через env ARTAS_DEFAULT_SYMBOL
            // (используется run.ps1 -Symbol).
            var defaultSymbol = System.Environment.GetEnvironmentVariable("ARTAS_DEFAULT_SYMBOL");
            if (!string.IsNullOrWhiteSpace(defaultSymbol))
            {
                viewModel.SymbolName = defaultSymbol;
            }

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
