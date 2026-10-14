using System;
using ArtasChart.App.ViewModels;
using ArtasChart.App.Views;
using ArtasCore.Models;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ArtasChart.App;

public partial class MainWindow : Window
{
    private FractalChartControl? _chart;
    private Button? _autoScaleYButton;

    public MainWindow()
    {
        InitializeComponent();

        _chart = this.FindControl<FractalChartControl>("Chart");
        if (_chart is not null)
        {
            // Единственная «техническая» связь View→VM: проброс события наведения
            // курсора (ScottPlot не предоставляет bindable-свойство для этого).
            _chart.FractalHovered += OnFractalHovered;
        }

        // Кнопка автомасштаба по Y: подгоняет Y под текущую видимую область X
        // на обоих графиках (надёжная альтернатива двойному клику).
        _autoScaleYButton = this.FindControl<Button>("AutoScaleYButton");
        if (_autoScaleYButton is not null)
        {
            _autoScaleYButton.Click += (_, _) => _chart?.AutoScaleY();
        }

        DataContextChanged += OnDataContextChanged;
    }

    private async void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && _chart is not null)
        {
            // Заполняем выпадающий список символов до загрузки данных, чтобы
            // пользователь мог выбрать нужный инструмент. await возвращает
            // управление на UI-поток (ContinueWith выполнял бы код в пуле потоков).
            await vm.LoadSymbolsAsync();

            // Автоматическая загрузка данных при старте окна и real-time
            // дозагрузка каждые 5 секунд. Состояние фильтров — в VM.
            if (vm.LoadCommand.CanExecute(null))
            {
                vm.LoadCommand.Execute(null);
            }

            vm.StartAutoRefresh(TimeSpan.FromSeconds(5));
        }
    }

    private void OnFractalHovered(Fractal? fractal)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.UpdateInfo(fractal);
        }
    }
}
