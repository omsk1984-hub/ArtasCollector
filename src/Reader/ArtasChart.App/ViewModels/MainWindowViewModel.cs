using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ArtasCore.Abstractions;
using Avalonia.Threading;
using ArtasCore.Models;
using ArtasChart.Data;
using ArtasChart.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArtasChart.App.ViewModels;

/// <summary>
/// ViewModel главного окна: загрузка фракталов из БД, фильтрация и подготовка
/// серии к отображению. Зависит только от абстракций (Data + Services) —
/// принцип инверсии зависимостей (D). Realtime-опрос делегирован
/// <see cref="IPollingService"/>, чтобы не зависеть от UI-фреймворка.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly IFractalReader _reader;
    private readonly IFractalSeriesPreparer _seriesPreparer;
    private readonly IFractalInfoFormatter _infoFormatter;
    private readonly IChartSeriesBuilder _seriesBuilder;
    private readonly IPollingService _polling;
    private readonly ISymbolFilterSettingsStore _settingsStore;

    /// <summary>Базовый суффикс заголовка окна (без символа).</summary>
    private const string WindowTitleSuffix = "ArtasChart (Avalonia + ScottPlot)";

    [ObservableProperty]
    private string _windowTitle = WindowTitleSuffix;

    [ObservableProperty]
    private string _symbolName = "Si";

    /// <summary>
    /// Список доступных символов (постоянные имена из таблицы symbols) для
    /// выпадающего списка. Заполняется при старте окна.
    /// </summary>
    public ObservableCollection<string> AvailableSymbols { get; } = new();

    [ObservableProperty]
    private int _limit = 5000;

    [ObservableProperty]
    private string _status = "Готово. Нажмите «Загрузить».";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _infoText = "Наведите курсор на точку...";

    /// <summary>Последний наведённый (выбранный) фрактал — для копирования.</summary>
    private Fractal? _selectedFractal;

    /// <summary>Текст для буфера обмена (компактная сводка выбранного фрактала).</summary>
    public string InfoClipboardText =>
        _infoFormatter.FormatClipboard(_selectedFractal);

    [ObservableProperty]
    private string _visibleCountText = string.Empty;

    // --- Параметры фильтрации (привязываются в XAML) ---

    /// <summary>Порог фильтра по Markets[0].Volume (Ask). 0 — фильтр выключен.</summary>
    [ObservableProperty]
    private double _minMarket0Volume;

    /// <summary>Порог фильтра по Markets[1].Volume (Bid). 0 — фильтр выключен.</summary>
    [ObservableProperty]
    private double _minMarket1Volume;

    /// <summary>Порог фильтра по Info.Volume. 0 — фильтр выключен.</summary>
    [ObservableProperty]
    private double _minVolume;

    /// <summary>Порог фильтра по Info.PickVolume. 0 — фильтр выключен.</summary>
    [ObservableProperty]
    private double _minPickVolume;

    /// <summary>Число соседей N для фильтра локального экстремума (0 — выключен).</summary>
    [ObservableProperty]
    private int _extremumNeighbors = 2;

    /// <summary>Значение N по умолчанию для фильтра локального экстремума.</summary>
    private const int DefaultExtremumNeighbors = 2;

    /// <summary>
    /// Подготовленная к отображению серия. View биндится/реагирует на изменение
    /// этого свойства вместо подписки на C#-события.
    /// </summary>
    [ObservableProperty]
    private ChartSeries? _series;

    /// <summary>Флаг: считать ли текущее обновление «первой загрузкой» (автомасштаб).</summary>
    [ObservableProperty]
    private bool _autoScaleRequested;

    /// <summary>Показывать ли красные точки (невзятия; по умолчанию скрыты).</summary>
    [ObservableProperty]
    private bool _showRedPoints;

    /// <summary>Полная загруженная серия фракталов (вход для фильтрации).</summary>
    private IReadOnlyList<Fractal> _allFractals = Array.Empty<Fractal>();

    /// <summary>
    /// Минимальный шаг цены текущего символа (<c>symbols.point</c>), загруженный
    /// при «Загрузить». Используется для выражения разности цен в пунктах (ΔY).
    /// 0 означает «неизвестно».
    /// </summary>
    private double _symbolPoint;

    private long _lastId;
    private int _lastIndex;

    public MainWindowViewModel(
        IFractalReader reader,
        IFractalSeriesPreparer seriesPreparer,
        IFractalInfoFormatter infoFormatter,
        IChartSeriesBuilder seriesBuilder,
        IPollingService polling,
        ISymbolFilterSettingsStore settingsStore)
    {
        _reader = reader;
        _seriesPreparer = seriesPreparer;
        _infoFormatter = infoFormatter;
        _seriesBuilder = seriesBuilder;
        _polling = polling;
        _settingsStore = settingsStore;
    }

    /// <summary>
    /// Запустить периодическую дозагрузку новых фракталов (real-time).
    /// </summary>
    public void StartAutoRefresh(TimeSpan interval)
    {
        _polling.Start(interval, _ => RefreshNewAsync());
    }

    /// <summary>Остановить периодическую дозагрузку.</summary>
    public void StopAutoRefresh() => _polling.Stop();

    /// <summary>
    /// Загрузить список доступных символов из БД и заполнить выпадающий список.
    /// Вызывается при старте окна до/независимо от загрузки фракталов.
    /// </summary>
    public async Task LoadSymbolsAsync()
    {
        try
        {
            var names = await _reader.GetSymbolNamesAsync();
            PopulateSymbols(names);
        }
        catch (Exception ex)
        {
            // Даже при ошибке БД выпадающий список должен показывать текущий
            // символ, иначе поле выглядит пустым.
            PopulateSymbols(Array.Empty<string>());
            Status = $"Не удалось загрузить список символов: {ex.Message}";
        }
    }

    /// <summary>
    /// Заполнить выпадающий список и гарантировать, что текущий <see cref="SymbolName"/>
    /// присутствует в нём и выбран (даже если БД вернула пустой список или не
    /// содержит текущий символ).
    /// </summary>
    private void PopulateSymbols(IReadOnlyList<string> names)
    {
        AvailableSymbols.Clear();
        foreach (var name in names)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                AvailableSymbols.Add(name);
            }
        }

        // Текущий символ должен быть в списке, чтобы ComboBox его отобразил.
        if (!string.IsNullOrWhiteSpace(SymbolName) && !AvailableSymbols.Contains(SymbolName))
        {
            AvailableSymbols.Insert(0, SymbolName);
        }

        // Если символ ещё не выбран (пустой) — берём первый доступный.
        if (string.IsNullOrWhiteSpace(SymbolName) && AvailableSymbols.Count > 0)
        {
            SymbolName = AvailableSymbols[0];
        }

        // ComboBox сбрасывает SelectedItem при изменении ItemsSource. Чтобы
        // привязка гарантированно переустановила текущий элемент, сбрасываем
        // значение и задаём его заново (через диспетчер — после того как
        // ComboBox обработает изменение коллекции).
        var selected = SymbolName;
        SymbolName = string.Empty;
        Dispatcher.UIThread.Post(() => SymbolName = selected);
    }

    /// <summary>Дозагрузить фракталы с id больше последнего загруженного.</summary>
    public async Task RefreshNewAsync()
    {
        if (IsBusy || _lastId <= 0)
        {
            return;
        }

        try
        {
            var fresh = await _reader.GetFractalsAsync(
                SymbolName, limit: 1000, fractalId: _lastId, orderDesc: false);

            if (fresh.Count == 0)
            {
                return;
            }

            // Продолжаем нумерацию индексов от последнего, чтобы новые точки
            // вставали правее уже отрисованных.
            _seriesPreparer.AssignIndices(fresh, _lastIndex);

            _lastId = fresh[^1].Id;
            _lastIndex = fresh[^1].Index;

            _allFractals = _allFractals.Concat(fresh).ToList();

            Status = $"Дозагружено {fresh.Count} новых фракталов (last id={_lastId}).";
            RebuildSeries(autoScale: false);
        }
        catch (Exception ex)
        {
            Status = $"Ошибка обновления: {ex.Message}";
        }
    }

    /// <summary>Загрузить фракталы из БД и инициировать отрисовку.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Status = "Загрузка фракталов...";
        try
        {
            var symbol = await _reader.GetSymbolInfoAsync(SymbolName);
            if (symbol is null)
            {
                Status = $"Символ '{SymbolName}' не найден.";
                return;
            }

            _symbolPoint = symbol.Point;

            var fractals = await _reader.GetFractalsAsync(
                SymbolName, limit: Limit, orderDesc: true);

            // Присваиваем порядковые номера (X-координата на графике): 1..N.
            _seriesPreparer.AssignIndices(fractals, 0);

            _allFractals = fractals;
            _lastId = fractals.Count > 0 ? fractals[^1].Id : 0;
            _lastIndex = fractals.Count > 0 ? fractals[^1].Index : 0;

            // Заголовок окна формируется только при загрузке по кнопке
            // «Загрузить» (real-time дозагрузка заголовок не трогает).
            WindowTitle = BuildWindowTitle(SymbolName);

            // Настройки фильтра при загрузке по кнопке НЕ подгружаются из БД —
            // используются текущие значения полей фильтра в VM.
            Status = $"Загружено {fractals.Count} фракталов.";
            RebuildSeries(autoScale: true);
        }
        catch (Exception ex)
        {
            Status = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Применить текущие параметры фильтрации, обновить серию и сохранить
    /// настройки фильтра для текущего символа в БД (таблица symbol_filter_settings).
    /// </summary>
    [RelayCommand]
    private async Task ApplyFiltersAsync()
    {
        if (_allFractals.Count == 0)
        {
            return;
        }

        RebuildSeries(autoScale: false);

        // Сохраняем настройки для символа, чтобы восстановить их при «Загрузить».
        try
        {
            await _settingsStore.SaveAsync(SymbolName, CaptureCurrentSettings());
            Status = $"Фильтр применён и сохранён для '{SymbolName}'.";
        }
        catch (Exception ex)
        {
            Status = $"Фильтр применён, но не сохранён: {ex.Message}";
        }
    }

    /// <summary>
    /// Реакция на выбор другого символа в выпадающем списке: подгружаем
    /// сохранённые настройки фильтра этого символа из БД, чтобы пользователь
    /// сразу видел его пороги. Данные (фракталы) НЕ перезагружаются — это
    /// выполняется по кнопке «Загрузить», чтобы не нагружать БД и UI.
    /// </summary>
    /// <remarks>
    /// Метод вызывается генератором <c>[ObservableProperty]</c> при каждом
    /// изменении <see cref="SymbolName"/>, в том числе при техническом сбросе
    /// в пустую строку внутри <see cref="PopulateSymbols"/> — такие срабатывания
    /// игнорируются, чтобы не делать лишних запросов к БД.
    /// </remarks>
    partial void OnSymbolNameChanged(string? oldValue, string newValue)
    {
        // Пустое значение — технический сброс при переустановке ItemsSource
        // ComboBox; реальной смены символа нет.
        if (string.IsNullOrWhiteSpace(newValue))
        {
            return;
        }

        // Best-effort: ошибки БД не должны ломать выбор символа в ComboBox.
        _ = ApplyFiltersForSelectedSymbolAsync(newValue);
    }

    /// <summary>
    /// Асинхронно загрузить и применить сохранённые фильтры для выбранного
    /// символа. Если сохранённых настроек нет — используются значения по умолчанию.
    /// </summary>
    private async Task ApplyFiltersForSelectedSymbolAsync(string symbolName)
    {
        try
        {
            await ApplySavedFiltersAsync(symbolName);
            Status = $"Загружены настройки фильтра для '{symbolName}'.";
        }
        catch (Exception ex)
        {
            Status = $"Не удалось загрузить настройки фильтра для '{symbolName}': {ex.Message}";
        }
    }

    /// <summary>Сбросить параметры фильтрации к значениям «выключено».</summary>
    public void ResetFilters()
    {
        MinMarket0Volume = 0;
        MinMarket1Volume = 0;
        MinVolume = 0;
        MinPickVolume = 0;
        ExtremumNeighbors = DefaultExtremumNeighbors;
        ShowRedPoints = false;
    }

    /// <summary>Снять текущие параметры фильтрации в DTO для сохранения.</summary>
    private FractalFilterSettings CaptureCurrentSettings() =>
        new(MinMarket0Volume, MinMarket1Volume, MinVolume, ExtremumNeighbors, ShowRedPoints,
            MinPickVolume, Limit);

    /// <summary>
    /// Загрузить сохранённые настройки фильтра символа и заполнить поля VM.
    /// Если настройки отсутствуют — сбросить фильтры к значениям по умолчанию.
    /// </summary>
    private async Task ApplySavedFiltersAsync(string symbolName)
    {
        var settings = await _settingsStore.LoadAsync(symbolName);
        if (settings is null)
        {
            ResetFilters();
            return;
        }

        MinMarket0Volume = settings.MinMarket0Volume;
        MinMarket1Volume = settings.MinMarket1Volume;
        MinVolume = settings.MinVolume;
        MinPickVolume = settings.MinPickVolume;
        ExtremumNeighbors = settings.ExtremumNeighbors;
        ShowRedPoints = settings.ShowRedPoints;
        Limit = settings.LoadLimit > 0 ? settings.LoadLimit : Limit;
    }

    /// <summary>Обновить info-панель по выбранному (наведённому) фракталу.</summary>
    public void UpdateInfo(Fractal? fractal)
    {
        _selectedFractal = fractal;
        InfoText = _infoFormatter.Format(fractal);

        // Уведомляем привязки о том, что текст для копирования изменился.
        OnPropertyChanged(nameof(InfoClipboardText));
    }

    /// <summary>
    /// Пересобрать серию по текущим фильтрам и опубликовать результат в <see cref="Series"/>.
    /// </summary>
    private void RebuildSeries(bool autoScale)
    {
        var options = new FractalFilterOptions(
            MinMarket0Volume, MinMarket1Volume, MinVolume, MinPickVolume);

        var series = _seriesBuilder.Build(_allFractals, options, ExtremumNeighbors, _symbolPoint);
        UpdateVisibleCount(series.VisibleCount, series.TotalCount);

        AutoScaleRequested = autoScale || Series is null;
        Series = series;
    }

    private void UpdateVisibleCount(int visible, int total)
    {
        VisibleCountText = total > 0 ? $"Видимых: {visible} из {total}" : string.Empty;
    }

    /// <summary>
    /// Сформировать заголовок окна: загруженный символ в начале, затем базовый
    /// суффикс (например, «Si ArtasChart (Avalonia + ScottPlot)»). Если символ
    /// пуст — возвращается только суффикс.
    /// </summary>
    private static string BuildWindowTitle(string symbol)
        => string.IsNullOrWhiteSpace(symbol)
            ? WindowTitleSuffix
            : $"{symbol.Trim()} {WindowTitleSuffix}";
}
