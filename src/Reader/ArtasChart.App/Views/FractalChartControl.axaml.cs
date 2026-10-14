using System;
using System.Collections.Generic;
using System.Linq;
using ArtasCore.Models;
using ArtasChart.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Interactivity;
using ScottPlot.Plottables;

namespace ArtasChart.App.Views;

/// <summary>
/// Состояние интерактивного измерителя расстояния (клик-клик-сброс).
/// </summary>
internal enum MeasureState
{
    /// <summary>Измерение не начато — следующий клик задаёт точку A.</summary>
    None,

    /// <summary>Точка A задана — следующий клик задаёт точку B и показывает измерение.</summary>
    HasA,

    /// <summary>Точки A и B заданы — следующий клик сбрасывает измерение.</summary>
    HasB,
}

/// <summary>
/// Контрол отображения фракталов: верхний график (линия + точки, index × price)
/// и нижняя гистограмма From Start Interest, синхронизированные по оси X.
/// Вся бизнес-логика (фильтрация, подготовка серии, тики, границы дней) вынесена
/// в <see cref="IChartSeriesBuilder"/> — контрол остаётся «тонким» представлением:
/// он только рисует готовую <see cref="ChartSeries"/> и обрабатывает ввод.
/// </summary>
public partial class FractalChartControl : UserControl
{
    /// <summary>Bindable-свойство: подготовленная серия для отрисовки.</summary>
    public static readonly StyledProperty<ChartSeries?> SeriesProperty =
        AvaloniaProperty.Register<FractalChartControl, ChartSeries?>(nameof(Series));

    /// <summary>Bindable-свойство: запрос автомасштаба (при первой загрузке группы).</summary>
    public static readonly StyledProperty<bool> AutoScaleRequestedProperty =
        AvaloniaProperty.Register<FractalChartControl, bool>(nameof(AutoScaleRequested));

    /// <summary>Bindable-свойство: показывать ли красные точки (невзятия).</summary>
    public static readonly StyledProperty<bool> ShowRedPointsProperty =
        AvaloniaProperty.Register<FractalChartControl, bool>(nameof(ShowRedPoints));

    private AvaPlot? _mainPlot;
    private AvaPlot? _histPlot;
    private DispatcherTimer? _syncTimer;

    private List<Fractal> _allFractals = new();
    private List<Fractal> _visibleFractals = new();
    private string _title = "Фракталы";

    /// <summary>
    /// Последняя отрисованная серия (массивы для линии и точек подготовлены
    /// в <see cref="IChartSeriesBuilder"/>; контроль только рисует).
    /// </summary>
    private ChartSeries? _series;

    private Scatter? _line;
    private Scatter? _takePoints;
    private Scatter? _otherPoints;
    private Scatter? _greenPoints;
    private BarPlot? _histBars;
    private Crosshair? _crosshair;

    /// <summary>Масштабная линейка (ruler) для оценки расстояний по X и Y.</summary>
    private ScaleBar? _scaleBar;

    /// <summary>Последние «круглые» размеры линейки (для решения о Refresh).</summary>
    private double _lastScaleWidth = double.NaN;
    private double _lastScaleHeight = double.NaN;

    // --- Интерактивный измеритель расстояния (click-to-measure) ---

    /// <summary>Текущее состояние измерителя: нет / задана точка A / заданы A и B.</summary>
    private MeasureState _measureState = MeasureState.None;

    /// <summary>Координаты точки A измерителя (data-координаты).</summary>
    private Coordinates _measureA;

    /// <summary>Координаты точки B измерителя (data-координаты).</summary>
    private Coordinates _measureB;

    /// <summary>Точка нажатия ЛКМ (экранные координаты) — для отличия клика от перетаскивания.</summary>
    private Point _pointerDownPosition;

    /// <summary>Порог (в пикселях), ниже которого нажатие+отпускание считается кликом, а не панорамированием.</summary>
    private const double ClickThresholdPx = 5.0;

    // Плоты измерителя (пересоздаются после Clear()).
    private LinePlot? _measureLine;
    private Scatter? _measureMarkers;
    private Text? _measureText;

    /// <summary>Курсор-подсветка выбранного фрактала на гистограмме.</summary>
    private VerticalLine? _histCrosshair;

    /// <summary>Вертикальные линии начала дня на основном графике.</summary>
    private readonly List<VerticalLine> _dayLines = new();

    /// <summary>Вертикальные линии начала дня на гистограмме.</summary>
    private readonly List<VerticalLine> _histDayLines = new();

    /// <summary>
    /// Последний автоматически рассчитанный Y-диапазон гистограммы
    /// (для определения, менял ли пользователь масштаб вручную).
    /// </summary>
    private AxisLimits? _lastHistAutoLimits;

    /// <summary>
    /// Пользователь вручную менял масштаб гистограммы по Y — авто-масштаб отключён,
    /// пока не будет применён новый фильтр/загрузка.
    /// </summary>
    private bool _histYManual;

    /// <summary>Событие выбора фрактала (для обновления внешней info-панели).</summary>
    public event Action<Fractal?>? FractalHovered;

    /// <summary>Заголовок графика (символ/пояснение).</summary>
    public string Title
    {
        get => _title;
        set
        {
            _title = value;
            _mainPlot?.Plot.Title(_title);
            _mainPlot?.Refresh();
        }
    }

    /// <summary>Подготовленная серия для отрисовки.</summary>
    public ChartSeries? Series
    {
        get => GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    /// <summary>Запрос автомасштаба при следующей отрисовке.</summary>
    public bool AutoScaleRequested
    {
        get => GetValue(AutoScaleRequestedProperty);
        set => SetValue(AutoScaleRequestedProperty, value);
    }

    /// <summary>Показывать ли красные точки (невзятия).</summary>
    public bool ShowRedPoints
    {
        get => GetValue(ShowRedPointsProperty);
        set => SetValue(ShowRedPointsProperty, value);
    }

    public FractalChartControl()
    {
        InitializeComponent();

        _mainPlot = this.FindControl<AvaPlot>("MainPlot");
        _histPlot = this.FindControl<AvaPlot>("HistPlot");

        ConfigurePlots();

        // Страховка: если на момент конструктора UserInputProcessor ещё не был
        // доступен, повторяем удаление встроенного автомасштаба после загрузки.
        Loaded += (_, _) =>
        {
            if (_mainPlot is not null)
            {
                DisableBuiltInClickAutoscale(_mainPlot);
            }

            if (_histPlot is not null)
            {
                DisableBuiltInClickAutoscale(_histPlot);
            }
        };
    }

    static FractalChartControl()
    {
        SeriesProperty.Changed.AddClassHandler<FractalChartControl>((control, _) =>
            control.OnSeriesChanged());

        ShowRedPointsProperty.Changed.AddClassHandler<FractalChartControl>((control, _) =>
            control.OnShowRedPointsChanged());
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void ConfigurePlots()
    {
        if (_mainPlot is null || _histPlot is null)
        {
            return;
        }

        // Убираем встроенный автомасштаб ScottPlot по двойному клику: он
        // масштабирует обе оси и мешал бы предсказуемой работе автомасштаба
        // по Y, который теперь вызывается кнопкой «Автомасштаб Y».
        DisableBuiltInClickAutoscale(_mainPlot);
        DisableBuiltInClickAutoscale(_histPlot);

        var main = _mainPlot.Plot;
        // Верхняя подпись графика не показывается (заголовок окна заменяет её).
        main.Axes.Title.IsVisible = false;
        main.YLabel("Цена (пик фрактала)");

        _crosshair = main.Add.Crosshair(0, 0);
        _crosshair.IsVisible = false;

        AddScaleBar(main);

        _mainPlot.PointerMoved += OnPointerMoved;

        // Интерактивный измеритель: быстрый клик ЛКМ (без перетаскивания) —
        // шаг измерения; зажатие с перетаскиванием остаётся панорамированием.
        _mainPlot.PointerPressed += OnMainPointerPressed;
        _mainPlot.PointerReleased += OnMainPointerReleased;

        var hist = _histPlot.Plot;
        hist.YLabel("From Start Interest");
        // Скрываем ось X у гистограммы (общая шкала — на верхнем графике).
        hist.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.EmptyTickGenerator();

        // Курсор-подсветка выбранного фрактала на гистограмме.
        _histCrosshair = hist.Add.VerticalLine(0);
        _histCrosshair.IsVisible = false;

        // Наведение мыши по гистограмме обновляет info-панель так же, как верхний график.
        _histPlot.PointerMoved += OnHistPointerMoved;

        // Пользователь вручную меняет масштаб гистограммы колесом мыши —
        // запоминаем это, чтобы отключить авто-масштаб по Y.
        _histPlot.PointerWheelChanged += (_, _) => _histYManual = true;
        _histPlot.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(_histPlot).Properties.IsMiddleButtonPressed ||
                e.GetCurrentPoint(_histPlot).Properties.IsRightButtonPressed)
            {
                _histYManual = true;
            }
        };

        // Периодическая синхронизация оси X гистограммы с основным графиком.
        _syncTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _syncTimer.Tick += (_, _) => SyncHistToMain();
        _syncTimer.Start();
    }

    /// <summary>
    /// Убрать встроенный автомасштаб ScottPlot по двойному клику.
    /// ScottPlot сам реагирует на двойной клик вызовом <c>Axes.AutoScale()</c>,
    /// перетирая наш автомасштаб по Y и возвращая график к исходному виду.
    /// Удаляем соответствующие реакции из <see cref="UserInputProcessor.UserActionResponses"/>,
    /// сохраняя панорамирование, зум колесом и зум прямоугольником.
    /// </summary>
    private static void DisableBuiltInClickAutoscale(AvaPlot plot)
    {
        var responses = plot.Plot.PlotControl?.UserInputProcessor?.UserActionResponses;
        if (responses is null)
        {
            return;
        }

        for (var i = responses.Count - 1; i >= 0; i--)
        {
            var name = responses[i]?.GetType().Name;
            if (name == "DoubleClickResponse" || name == "SingleClickAutoscale")
            {
                responses.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Реакция на смену флага «Показать»: переключаем видимость цветных точек
    /// (красные — по Min Volume, зелёные — по Min Markets Ask/Bid) без полной
    /// перерисовки.
    /// </summary>
    private void OnShowRedPointsChanged()
    {
        if (_otherPoints is not null)
        {
            _otherPoints.IsVisible = ShowRedPoints;
        }

        if (_greenPoints is not null)
        {
            _greenPoints.IsVisible = ShowRedPoints;
        }

        _mainPlot?.Refresh();
    }

    /// <summary>
    /// Реакция на смену серии: запоминаем наборы и перерисовываем.
    /// </summary>
    private void OnSeriesChanged()
    {
        var series = Series;
        _series = series;

        if (series is null)
        {
            _allFractals = new List<Fractal>();
            _visibleFractals = new List<Fractal>();
            ApplyFilterAndRender(autoScale: false);
            return;
        }

        _allFractals = series.AllFractals.ToList();
        _visibleFractals = series.VisibleFractals.ToList();
        Render(autoScale: AutoScaleRequested);
    }

    /// <summary>
    /// Отрисовать готовую серию. При первой загрузке группы — автомасштаб осей.
    /// </summary>
    private void Render(bool autoScale)
        => ApplyFilterAndRender(autoScale);

    /// <summary>
    /// Перерисовать графики по текущим наборам (видимые/полные) без фильтрации:
    /// фильтрация уже выполнена в <see cref="IChartSeriesBuilder"/>.
    /// </summary>
    public void ApplyFilterAndRender(bool autoScale = false)
    {
        if (_mainPlot is null || _histPlot is null)
        {
            return;
        }

        var main = _mainPlot.Plot;
        var hist = _histPlot.Plot;

        // Сохраняем текущий масштаб и сдвиг осей, чтобы график не «прыгал».
        AxisLimits mainLimitsBefore = main.Axes.GetLimits();
        AxisLimits histLimitsBefore = hist.Axes.GetLimits();
        bool hadDataBefore = _visibleFractals.Count > 0;

        // Новая загрузка группы — сбрасываем ручной масштаб гистограммы.
        if (autoScale)
        {
            _histYManual = false;
            _lastHistAutoLimits = null;
        }

        main.Clear();
        hist.Clear();

        // Плоты точек удалены вместе с остальными — сбрасываем ссылки,
        // чтобы обработчик флага «Показать» не трогал мёртвые объекты.
        _otherPoints = null;
        _greenPoints = null;

        // Масштабная линейка тоже удалена Clear() — сбрасываем кэш размеров.
        _scaleBar = null;
        _lastScaleWidth = double.NaN;
        _lastScaleHeight = double.NaN;

        // Плоты измерителя удалены Clear() — сбрасываем только ссылки,
        // а состояние (A/B) сохраняем: измеритель восстановится после
        // перерисовки, чтобы новое поступление тика его не стирало.
        _measureLine = null;
        _measureMarkers = null;
        _measureText = null;

        // Пересоздаём статичные элементы, удалённые Clear().
        _crosshair = main.Add.Crosshair(0, 0);
        _crosshair.IsVisible = false;

        AddScaleBar(main);

        if (_visibleFractals.Count == 0)
        {
            // Восстанавливаем измеритель и на пустом графике (данные могли
            // отфильтроваться, но измерение пользователь не сбрасывал).
            if (_measureState != MeasureState.None)
            {
                DrawMeasure(preview: false);
            }

            _mainPlot.Refresh();
            _histPlot.Refresh();
            return;
        }

        // Массивы подготовлены в ChartSeriesBuilder:
        //  • Xs/Prices — линия по всем вершинам (после экстремум-фильтра);
        //  • TakeXs/TakeYs — синие точки (взятия), независимо от объёма;
        //  • OtherXs/OtherYs — красные точки (невзятия), только прошедшие объёмный фильтр.
        var series = _series;
        if (series is null)
        {
            return;
        }

        var xs = series.Xs;
        var prices = series.Prices;

        // Синяя линия по вершинам фракталов отображается всегда.
        _line = main.Add.ScatterLine(xs, prices);
        _line.Color = Color.FromHex("#AAAAAA");
        _line.LineWidth = 1;
        _line.MarkerSize = 0;

        _takePoints = main.Add.Scatter(series.TakeXs, series.TakeYs);
        _takePoints.Color = Color.FromHex("#0000FF");
        _takePoints.MarkerSize = 5;

        // Зелёные точки (невзятия, прошли фильтр Min Markets Ask/Bid) добавляются
        // ПЕРВЫМИ, чтобы оказаться ПОД красными: крупнее и полупрозрачные, при
        // совпадении видно зелёное кольцо вокруг красной точки.
        _greenPoints = main.Add.Scatter(series.GreenXs, series.GreenYs);
        _greenPoints.Color = Color.FromHex("#00AA00").WithAlpha(120);
        _greenPoints.MarkerSize = 9;
        _greenPoints.LineWidth = 0;
        _greenPoints.LineStyle = LineStyle.None;
        // Видимость зелёных точек — тот же флаг «Показать».
        _greenPoints.IsVisible = ShowRedPoints;

        // Красные точки (невзятия, прошли фильтр Min Volume) — поверх зелёных.
        _otherPoints = main.Add.Scatter(series.OtherXs, series.OtherYs);
        _otherPoints.Color = Color.FromHex("#FF0000");
        _otherPoints.MarkerSize = 5;
        // Без соединяющих линий между красными точками.
        _otherPoints.LineWidth = 0;
        _otherPoints.LineStyle = LineStyle.None;
        // Видимость красных точек управляется флагом «Показать».
        _otherPoints.IsVisible = ShowRedPoints;

        var bars = new List<Bar>();
        for (var i = 0; i < _visibleFractals.Count; i++)
        {
            var f = _visibleFractals[i];
            var color = f.Stat.IsTake ? Color.FromHex("#0000FF") : Color.FromHex("#FF0000");
            bars.Add(new Bar
            {
                Position = xs[i],
                ValueBase = 0,
                Value = (double)f.FromStartInterest,
                FillColor = color,
                // Без обводки столбцов: ScottPlot по умолчанию рисует контур
                // (для красных столбцов — красный), из-за чего гистограмма
                // выглядела «обведённой». Отключаем линию контура.
                LineWidth = 0,
                LineColor = Colors.Transparent,
            });
        }

        _histBars = hist.Add.Bars(bars);

        // Вертикальные линии начала каждого дня.
        DrawDayBoundaries(main, hist);

        if (autoScale || !hadDataBefore)
        {
            main.Axes.AutoScale();
            hist.Axes.AutoScaleX();
            _lastHistAutoLimits = null;
            AutoScaleHistY(main.Axes.GetLimits());
        }
        else
        {
            main.Axes.SetLimits(mainLimitsBefore);
            hist.Axes.SetLimits(histLimitsBefore);
        }

        RefreshIntegerTicks();

        // Восстанавливаем интерактивный измеритель на новых плотах, если он был
        // активен (иначе поступление нового тика стирало бы линию измерения).
        if (_measureState != MeasureState.None)
        {
            DrawMeasure(preview: false);
        }

        _mainPlot.Refresh();
        _histPlot.Refresh();
    }

    /// <summary>
    /// Дискретная ось X: целочисленные индексы фракталов, подписи — время в МСК (HH:mm).
    /// На границе дня подпись включает дату (dd.MM HH:mm).
    /// </summary>
    private void RefreshIntegerTicks()
    {
        if (_mainPlot is null)
        {
            return;
        }

        var limits = _mainPlot.Plot.Axes.GetLimits();
        double left = limits.Left;
        double right = limits.Right;

        int span = (int)Math.Ceiling(right - left);
        int step = Math.Max(1, (int)Math.Ceiling(span / 12.0));
        int first = (int)Math.Ceiling(left / step) * step;

        var indexToTime = BuildIndexToTimeMap();
        DateTime? prevTickDay = null;

        var ticks = new List<double>();
        var labels = new List<string>();
        for (int t = first; t <= right; t += step)
        {
            ticks.Add(t);

            if (indexToTime.TryGetValue(t, out var time))
            {
                var day = time.Date;
                var isDayStart = prevTickDay is not null && day != prevTickDay;
                prevTickDay = day;

                labels.Add(isDayStart
                    ? time.ToString("dd.MM HH:mm")
                    : time.ToString("HH:mm"));
            }
            else
            {
                labels.Add(string.Empty);
            }
        }

        _mainPlot.Plot.Axes.Bottom.SetTicks(ticks.ToArray(), labels.ToArray());
    }

    /// <summary>
    /// Карта Index → время пика по полной серии (сетка индексов стабильна,
    /// включая невидимые фильтрованные фракталы).
    /// </summary>
    private Dictionary<int, DateTime> BuildIndexToTimeMap()
    {
        var map = new Dictionary<int, DateTime>(_allFractals.Count);
        foreach (var f in _allFractals)
        {
            map[f.Index] = f.TickPeak.TimeMsk;
        }

        return map;
    }

    /// <summary>
    /// Синхронизировать гистограмму с основным графиком: по X — видимый диапазон,
    /// по Y — авто-масштаб под видимую область X (если пользователь не задал Y вручную).
    /// </summary>
    private void SyncHistToMain()
    {
        if (_mainPlot is null || _histPlot is null)
        {
            return;
        }

        var mainLimits = _mainPlot.Plot.Axes.GetLimits();
        var histLimits = _histPlot.Plot.Axes.GetLimits();

        var xChanged =
            Math.Abs(histLimits.Left - mainLimits.Left) > 1e-9 ||
            Math.Abs(histLimits.Right - mainLimits.Right) > 1e-9;

        if (xChanged)
        {
            _histPlot.Plot.Axes.SetLimitsX(mainLimits.Left, mainLimits.Right);
        }

        var yChanged = AutoScaleHistY(mainLimits);

        if (xChanged || yChanged)
        {
            _histPlot.Refresh();
        }

        // Масштабная линейка пересчитывается под текущий видимый диапазон
        // (зум/панорамирование меняют «круглые» значения X и Y).
        var scaleChanged = AddScaleBar(_mainPlot.Plot);
        if (scaleChanged)
        {
            _mainPlot.Refresh();
        }

        RefreshIntegerTicks();
    }

    /// <summary>
    /// Публичная точка входа для кнопки «Автомасштаб Y»: подгоняет масштаб по Y
    /// для обоих графиков под текущий видимый X-диапазон (X не меняется).
    /// </summary>
    public void AutoScaleY()
    {
        AutoScaleMainY();
        AutoScaleHistYForDoubleClick();
    }

    /// <summary>
    /// Автомасштаб Y основного графика по вызову двойного клика:
    /// текущий X-диапазон не меняется, Y подбирается по ценам вершин,
    /// видимых в этом диапазоне (с небольшим отступом).
    /// </summary>
    private void AutoScaleMainY()
    {
        if (_mainPlot is null || _visibleFractals.Count == 0)
        {
            return;
        }

        var main = _mainPlot.Plot;
        var limits = main.Axes.GetLimits();
        double minPrice = double.MaxValue;
        double maxPrice = double.MinValue;

        foreach (var f in _visibleFractals)
        {
            if (f.Index < limits.Left || f.Index > limits.Right)
            {
                continue;
            }

            if (f.Price < minPrice)
            {
                minPrice = f.Price;
            }

            if (f.Price > maxPrice)
            {
                maxPrice = f.Price;
            }
        }

        if (minPrice > maxPrice)
        {
            return;
        }

        var span = maxPrice - minPrice;
        var pad = span > 0 ? span * 0.05 : Math.Max(Math.Abs(maxPrice) * 0.05, 1);
        var yMin = minPrice - (minPrice < 0 ? pad : 0);
        var yMax = maxPrice + (maxPrice > 0 ? pad : 0);

        main.Axes.SetLimitsY(yMin, yMax);
        AutoScaleHistY(limits);
        _mainPlot.Refresh();
        _histPlot?.Refresh();
    }

    /// <summary>
    /// Обработка двойного клика по гистограмме: сброс ручного масштаба
    /// по Y и пере-автомасштаб под видимый X-диапазон.
    /// </summary>
    private void AutoScaleHistYForDoubleClick()
    {
        if (_histPlot is null || _mainPlot is null)
        {
            return;
        }

        _histYManual = false;
        _lastHistAutoLimits = null;

        if (AutoScaleHistY(_mainPlot.Plot.Axes.GetLimits()))
        {
            _histPlot.Refresh();
        }
    }

    /// <summary>
    /// Автоматически подобрать Y-диапазон гистограммы по FromStartInterest
    /// фракталов, видимых в текущем X-диапазоне основного графика.
    /// </summary>
    private bool AutoScaleHistY(AxisLimits mainLimits)
    {
        if (_histPlot is null || _histYManual || _visibleFractals.Count == 0)
        {
            return false;
        }

        double min = double.MaxValue;
        double max = double.MinValue;

        foreach (var f in _visibleFractals)
        {
            if (f.Index < mainLimits.Left || f.Index > mainLimits.Right)
            {
                continue;
            }

            var v = f.FromStartInterest;
            if (v < min)
            {
                min = v;
            }

            if (v > max)
            {
                max = v;
            }
        }

        if (min > max)
        {
            return false;
        }

        var span = max - min;
        var pad = span > 0 ? span * 0.05 : Math.Max(Math.Abs(max) * 0.05, 1);
        var yMin = min - (min < 0 ? pad : 0);
        var yMax = max + (max > 0 ? pad : 0);

        var newLimits = new AxisLimits(mainLimits.Left, mainLimits.Right, yMin, yMax);

        if (_lastHistAutoLimits is { } last &&
            Math.Abs(last.Bottom - yMin) < 1e-9 &&
            Math.Abs(last.Top - yMax) < 1e-9)
        {
            return false;
        }

        _lastHistAutoLimits = newLimits;
        _histPlot.Plot.Axes.SetLimitsY(yMin, yMax);
        return true;
    }

    /// <summary>
    /// Добавить (или пересоздать после <c>Clear()</c>) масштабную линейку в углу
    /// верхнего графика. Размер отрезка подбирается по текущему видимому диапазону:
    /// по X — около 1/10 диапазона индексов, по Y — около 1/10 диапазона цен,
    /// чтобы линейка оставалась наглядной при любом масштабе.
    /// </summary>
    private bool AddScaleBar(Plot main)
    {
        var limits = main.Axes.GetLimits();

        var spanX = Math.Abs(limits.Right - limits.Left);
        var spanY = Math.Abs(limits.Top - limits.Bottom);

        // Ширина/высота линейки — «круглые» значения около 1/10 диапазона.
        var width = Math.Max(1, RoundToNice(spanX / 10.0));
        var height = Math.Max(1e-9, RoundToNice(spanY / 10.0));

        if (_scaleBar is null)
        {
            _scaleBar = main.Add.ScaleBar(width, height);
        }
        else
        {
            _scaleBar.Width = width;
            _scaleBar.Height = height;
        }

        _scaleBar.XLabel = $"X: {width:0.##}";
        _scaleBar.YLabel = $"Y: {height:0.####}";

        // Сообщаем, изменились ли «круглые» размеры — для решения о Refresh.
        var changed =
            Math.Abs(_lastScaleWidth - width) > 1e-12 ||
            Math.Abs(_lastScaleHeight - height) > 1e-12;
        _lastScaleWidth = width;
        _lastScaleHeight = height;
        return changed;
    }

    /// <summary>
    /// Округлить значение до «красивой» шкалы (1/2/5 × 10^n) для подписей линейки.
    /// </summary>
    private static double RoundToNice(double value)
    {
        if (value <= 0 || double.IsNaN(value) || double.IsInfinity(value))
        {
            return 1;
        }

        var exponent = Math.Floor(Math.Log10(value));
        var magnitude = Math.Pow(10, exponent);
        var normalized = value / magnitude;

        var nice = normalized switch
        {
            < 1.5 => 1.0,
            < 3.5 => 2.0,
            < 7.5 => 5.0,
            _ => 10.0,
        };

        return nice * magnitude;
    }

    /// <summary>
    /// Нарисовать вертикальные линии в начале каждого дня (смена даты пика в МСК).
    /// </summary>
    private void DrawDayBoundaries(Plot main, Plot hist)
    {
        _dayLines.Clear();
        _histDayLines.Clear();

        if (_visibleFractals.Count == 0)
        {
            return;
        }

        var color = Color.FromHex("#FF8800");
        DateTime? currentDay = null;

        for (var i = 0; i < _visibleFractals.Count; i++)
        {
            var day = _visibleFractals[i].TickPeak.TimeMsk.Date;
            if (currentDay is null)
            {
                currentDay = day;
                continue;
            }

            if (day != currentDay)
            {
                currentDay = day;
                var x = _visibleFractals[i].Index - 0.5;

                var mainLine = main.Add.VerticalLine(x);
                mainLine.Color = color;
                mainLine.LineWidth = 2;
                _dayLines.Add(mainLine);

                var histLine = hist.Add.VerticalLine(x);
                histLine.Color = color;
                histLine.LineWidth = 2;
                _histDayLines.Add(histLine);
            }
        }
    }

    /// <summary>
    /// Нажатие ЛКМ на верхнем графике: запоминаем экранную позицию для отличия
    /// быстрого клика (измерение) от перетаскивания (панорамирование).
    /// </summary>
    private void OnMainPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_mainPlot is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(_mainPlot);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _pointerDownPosition = point.Position;
    }

    /// <summary>
    /// Отпускание ЛКМ на верхнем графике. Если курсор почти не сместился —
    /// это клик: шаг интерактивного измерителя. Иначе — панорамирование (игнор).
    /// </summary>
    private void OnMainPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_mainPlot is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(_mainPlot);
        if (point.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased)
        {
            return;
        }

        var dx = point.Position.X - _pointerDownPosition.X;
        var dy = point.Position.Y - _pointerDownPosition.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);

        // Перетаскивание — пользователь панорамировал, измерение не запускаем.
        if (distance > ClickThresholdPx)
        {
            return;
        }

        // Точка измерения берётся ровно из позиции перекрестия (_crosshair),
        // которое при наведении уже «прилипает» к вершине фрактала.
        // Фолбэк — координаты курсора, если перекрестие недоступно.
        if (_crosshair is { IsVisible: true })
        {
            AdvanceMeasure(_crosshair.Position);
        }
        else
        {
            var pixel = new Pixel(point.Position.X, point.Position.Y);
            var coords = _mainPlot.Plot.GetCoordinates(pixel);
            AdvanceMeasure(coords);
        }
    }

    /// <summary>
    /// Шаг интерактивного измерителя: A → B → сброс.
    /// Первый клик задаёт точку A, второй — точку B и показывает расстояние,
    /// третий — сбрасывает измерение.
    /// </summary>
    private void AdvanceMeasure(Coordinates point)
    {
        switch (_measureState)
        {
            case MeasureState.None:
                _measureA = point;
                _measureState = MeasureState.HasA;
                DrawMeasure(preview: false);
                break;

            case MeasureState.HasA:
                _measureB = point;
                _measureState = MeasureState.HasB;
                DrawMeasure(preview: false);
                break;

            default:
                // Третий клик — сброс и начало нового измерения не запускаем.
                ResetMeasure();
                break;
        }
    }

    /// <summary>
    /// Сбросить измерение: убрать плоты и вернуться в исходное состояние.
    /// </summary>
    private void ResetMeasure()
    {
        _measureState = MeasureState.None;

        if (_mainPlot is not null)
        {
            if (_measureLine is not null)
            {
                _mainPlot.Plot.Remove(_measureLine);
            }

            if (_measureMarkers is not null)
            {
                _mainPlot.Plot.Remove(_measureMarkers);
            }

            if (_measureText is not null)
            {
                _mainPlot.Plot.Remove(_measureText);
            }

            _mainPlot.Refresh();
        }

        _measureLine = null;
        _measureMarkers = null;
        _measureText = null;
    }

    /// <summary>
    /// Нарисовать текущее состояние измерителя: точку A (если задана) или
    /// отрезок A–B с подписью ΔX (индексы и время) и ΔY (цена).
    /// </summary>
    /// <param name="preview">Зарезервировано для промежуточной отрисовки.</param>
    private void DrawMeasure(bool preview)
    {
        if (_mainPlot is null)
        {
            return;
        }

        var main = _mainPlot.Plot;
        if (_measureLine is not null)
        {
            main.Remove(_measureLine);
        }

        if (_measureMarkers is not null)
        {
            main.Remove(_measureMarkers);
        }

        if (_measureText is not null)
        {
            main.Remove(_measureText);
        }

        _measureLine = null;
        _measureMarkers = null;
        _measureText = null;

        if (_measureState == MeasureState.None)
        {
            _mainPlot.Refresh();
            return;
        }

        var color = Color.FromHex("#FFCC00");

        if (_measureState == MeasureState.HasA)
        {
            // Показана только точка A.
            _measureMarkers = main.Add.Scatter(
                new[] { _measureA.X }, new[] { _measureA.Y });
            _measureMarkers.Color = color;
            _measureMarkers.MarkerSize = 8;
            _measureMarkers.LineWidth = 0;
            _mainPlot.Refresh();
            return;
        }

        // Точки A и B заданы: отрезок + засечки + подпись расстояния.
        _measureLine = main.Add.Line(
            _measureA.X, _measureA.Y, _measureB.X, _measureB.Y);
        _measureLine.Color = color;
        _measureLine.LineWidth = 2;

        _measureMarkers = main.Add.Scatter(
            new[] { _measureA.X, _measureB.X },
            new[] { _measureA.Y, _measureB.Y });
        _measureMarkers.Color = color;
        _measureMarkers.MarkerSize = 8;
        _measureMarkers.LineWidth = 0;

        var dy = _measureB.Y - _measureA.Y;

        // ΔX — разница во времени пиков (МСК), взятого по индексу каждой точки.
        var text = BuildMeasureText(_measureA, _measureB, dy);

        _measureText = main.Add.Text(text, _measureB.X, _measureB.Y);
        _measureText.LabelFontColor = Colors.Black;
        _measureText.LabelFontSize = 14;
        _measureText.OffsetX = 8;
        _measureText.OffsetY = -8;

        _mainPlot.Refresh();
    }

    /// <summary>
    /// Сформировать подпись измерителя: ΔX — разница во времени пиков (МСК)
    /// между точками A и B, ΔY — разница цен, выраженная в пунктах как
    /// <c>(B.Price − A.Price) / symbol.point</c>. Если шаг цены неизвестен
    /// (point ≤ 0), ΔY выводится в «сырых» ценовых единицах. Если время для
    /// индекса неизвестно, показывается «—».
    /// </summary>
    private string BuildMeasureText(Coordinates a, Coordinates b, double dy)
    {
        // ΔY считается как разность цен, делённая на symbol.point (шаг цены).
        var point = _series?.Point ?? 0;
        var hasPoint = point > 0;
        var dyPoints = hasPoint ? dy / point : dy;
        var dyLabel = hasPoint ? $"{dyPoints:0.####} пт (цена/point)" : $"{dy:0.####} (цена)";
        var absDyLabel = hasPoint
            ? $"{Math.Abs(dyPoints):0.####} пт"
            : $"{Math.Abs(dy):0.####}";
        var indexToTime = BuildIndexToTimeMap();

        var aIndex = (int)Math.Round(a.X);
        var bIndex = (int)Math.Round(b.X);

        var hasA = indexToTime.TryGetValue(aIndex, out var timeA);
        var hasB = indexToTime.TryGetValue(bIndex, out var timeB);

        // Разность открытого интереса (TickPeak.Interest) фракталов A и B.
        var interest = BuildInterestDeltaText(aIndex, bIndex);

        string deltaX;
        string absX;
        if (hasA && hasB)
        {
            var delta = timeB - timeA;
            deltaX = FormatDuration(delta);
            absX = FormatDuration(delta.Duration());
        }
        else
        {
            deltaX = "—";
            absX = "—";
        }

        var aLabel = hasA ? timeA.ToString("dd.MM HH:mm:ss") : aIndex.ToString();
        var bLabel = hasB ? timeB.ToString("dd.MM HH:mm:ss") : bIndex.ToString();

        return
            $"A: {aLabel}\n" +
            $"B: {bLabel}\n" +
            $"ΔX: {deltaX}\n" +
            $"ΔY: {dyLabel}\n" +
            $"ΔInterest: {interest}\n" +
            $"|ΔX|: {absX}   |ΔY|: {absDyLabel}";
    }

    /// <summary>
    /// Разность открытого интереса (TickPeak.Interest) фракталов A и B по их
    /// индексам. Если фрактал не найден — возвращается «—».
    /// </summary>
    private string BuildInterestDeltaText(int aIndex, int bIndex)
    {
        var fractalsByIndex = _allFractals.ToDictionary(f => f.Index);

        if (fractalsByIndex.TryGetValue(aIndex, out var fa) &&
            fractalsByIndex.TryGetValue(bIndex, out var fb))
        {
            var delta = fb.Interest - fa.Interest;
            return $"{delta} (A: {fa.Interest}, B: {fb.Interest})";
        }

        return "—";
    }

    /// <summary>
    /// Форматировать длительность в читаемую строку: «1д 02:03:04» или «02:03:04».
    /// </summary>
    private static string FormatDuration(TimeSpan span)
    {
        var sign = span < TimeSpan.Zero ? "-" : string.Empty;
        span = span.Duration();

        var days = (int)span.TotalDays;
        var text = $"{span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}";
        return days > 0 ? $"{sign}{days}д {text}" : $"{sign}{text}";
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_mainPlot is null || _visibleFractals.Count == 0)
        {
            return;
        }

        var position = e.GetPosition(_mainPlot);
        var pixel = new Pixel(position.X, position.Y);
        var coordinates = _mainPlot.Plot.GetCoordinates(pixel);

        var nearest = FindNearestFractal(coordinates.X);

        if (nearest is not null && _crosshair is not null)
        {
            _crosshair.IsVisible = true;
            _crosshair.Position = new Coordinates(nearest.Index, nearest.Price);
            _mainPlot.Refresh();
        }

        FractalHovered?.Invoke(nearest);
    }

    /// <summary>
    /// Наведение мыши по гистограмме: info-панель и подсветка обновляются
    /// так же, как на верхнем графике. Поиск фрактала — по общей оси X (Index).
    /// Дополнительно крестик синхронно перемещается по верхнему графику.
    /// </summary>
    private void OnHistPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_histPlot is null || _mainPlot is null || _visibleFractals.Count == 0)
        {
            return;
        }

        var position = e.GetPosition(_histPlot);
        var pixel = new Pixel(position.X, position.Y);
        var coordinates = _histPlot.Plot.GetCoordinates(pixel);

        var nearest = FindNearestFractal(coordinates.X);

        if (nearest is not null && _histCrosshair is not null)
        {
            // Подсветка столбца на гистограмме.
            _histCrosshair.IsVisible = true;
            _histCrosshair.X = nearest.Index;
            _histPlot.Refresh();

            // Синхронное перемещение крестика по верхнему графику.
            if (_crosshair is not null)
            {
                _crosshair.IsVisible = true;
                _crosshair.Position = new Coordinates(nearest.Index, nearest.Price);
                _mainPlot.Refresh();
            }
        }

        FractalHovered?.Invoke(nearest);
    }

    /// <summary>
    /// Найти фрактал, соответствующий ближайшей вершине синей линии фракталов
    /// (а не отфильтрованным красным точкам). Перекрестие «прилипает» к вершине
    /// линии по X-координате (индексу); цена берётся из самой линии.
    /// </summary>
    private Fractal? FindNearestFractal(double x)
    {
        // Вершины синей линии: индексы series.Xs (baseline после экстремум-фильтра),
        // независимые от объёмного фильтра красных точек.
        var line = _series;
        if (line is null || line.Xs.Length == 0)
        {
            // Фолбэк: если линии нет — по видимой серии.
            return FindNearestIn(_visibleFractals, x);
        }

        var nearestIndex = FindNearestLineIndex(line.Xs, x);
        var fractal = _visibleFractals.FirstOrDefault(f => f.Index == nearestIndex)
            ?? _allFractals.FirstOrDefault(f => f.Index == nearestIndex);

        return fractal ?? FindNearestIn(_visibleFractals, x);
    }

    /// <summary>
    /// Индекс ближайшей вершины синей линии по X-координате (массив отсортирован).
    /// </summary>
    private static int FindNearestLineIndex(double[] lineXs, double x)
    {
        var bestIndex = lineXs[0];
        var bestDistance = double.MaxValue;
        foreach (var candidate in lineXs)
        {
            var distance = Math.Abs(candidate - x);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = candidate;
            }
        }

        return (int)Math.Round(bestIndex);
    }

    /// <summary>
    /// Фолбэк-поиск ближайшего фрактала в произвольной серии по индексу.
    /// </summary>
    private static Fractal? FindNearestIn(IReadOnlyList<Fractal> series, double x)
    {
        if (series.Count == 0)
        {
            return null;
        }

        var index = (int)Math.Round(x);
        return series.FirstOrDefault(f => f.Index == index)
            ?? series.OrderBy(f => Math.Abs(f.Index - index)).FirstOrDefault();
    }
}
