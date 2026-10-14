# ArtasChart (C#: Avalonia + ScottPlot, .NET 10)

Десктоп-визуализатор фракталов в архитектуре **MVVM**.

> Требуется .NET 10 SDK (целевой фреймворк задаётся центрально в `Directory.Build.props`).

## Слои решения

```
ArtasCore            — Models (POCO, без UI): Fractal, MyTick, InfoF, StatF, SymbolInfo, MarketPair
                       (общая библиотека src/Core/ArtasCore, отдельного ArtasChart.Core нет)
ArtasChart.Data      — Доступ к PostgreSQL: FractalRepository + сегрегированные интерфейсы
                       (IFractalReader, IFractalCounter, IHealthProbe), DbConfig
ArtasChart.Services  — Домен/презентация:
                       • IFractalFilter / FractalFilter           — объёмные фильтры
                       • ILocalExtremumFilter / LocalExtremumFilter — фильтр локального экстремума
                       • IFractalSeriesPreparer / FractalSeriesPreparer — индексы, FromStartInterest
                       • IFractalInfoFormatter / FractalInfoFormatter   — текст info-панели
                       • IChartSeriesBuilder / ChartSeriesBuilder  — сборка ChartSeries для графика
                       • IPollingService / PollingService          — периодический опрос (real-time)
                       • IDiagnosticsService / DiagnosticsService  — CLI --check-db
ArtasChart.App       — UI (Avalonia):
                       • ViewModels/MainWindowViewModel   — состояние + команды
                       • MainWindow (axaml/cs)            — View окна
                       • Views/FractalChartControl        — View графика (тонкий)
                       • Program.cs                       — DI-композиция
```

## Правила MVVM (принятые в проекте)

| Слой | Отвечает за | Не должен |
|------|-------------|-----------|
| **Model** (`Core`) | Данные фракталов | Знать об UI |
| **ViewModel** (`App/ViewModels`) | Состояние (`[ObservableProperty]`), команды (`[RelayCommand]`), вызов абстракций | Обращаться к контролам, использовать `Dispatcher`/таймеры UI, порождать C#-события для View |
| **View** (`App`, `App/Views`) | Рендер, ввод, биндинги | Содержать бизнес-логику, создавать сервисы через `new`, парсить контролы |
| **Services** (`Services`) | Домен и подготовка данных к отображению | Зависеть от UI-фреймворка |

## Поток данных

```
IFractalReader ──► MainWindowViewModel
                        │  (фильтры: MinMarket0Volume, MinMarket1Volume, MinVolume, ExtremumNeighbors)
                        ▼
                  IChartSeriesBuilder.Build()  ──► ChartSeries
                        │                             │
                        ▼                             ▼
                  IPollingService          MainWindow.axaml: Series="{Binding Series}"
                  (real-time 5 c)                     │
                                                      ▼
                                          FractalChartControl (StyledProperty Series)
                                                      │  only ScottPlot render + hover
                                                      ▼
                                          FractalHovered ──► vm.UpdateInfo(fractal) ──► InfoText
```

- **Загрузка**: `LoadCommand` → чтение из БД → `AssignIndices` → `RebuildSeries(autoScale: true)`.
- **Real-time**: `IPollingService` (PeriodicTimer) каждые 5 с → `RefreshNewAsync` → дозагрузка → `RebuildSeries(autoScale: false)`.
- **Фильтрация**: параметры фильтров — наблюдаемые свойства VM; `ApplyFiltersCommand` → `RebuildSeries`.
- **View**: получает готовую `ChartSeries` через bindable-свойство, только рисует.

## Что дало соответствие MVVM

- Убраны C#-события `FractalsLoaded` / `FractalsAppended` / `VisibleCountChanged`: связь View↔VM теперь через `Series`, `AutoScaleRequested`, `InfoText`, `VisibleCountText`.
- Бизнес/визуальная логика из `FractalChartControl` вынесена в `ChartSeriesBuilder`.
- Параметры фильтров и кнопка «Применить фильтр» переведены на биндинги и `RelayCommand`.
- `DispatcherTimer` убран из VM: периодичность через `IPollingService`.
- View больше не создаёт сервисы через `new` (DI).

## Запуск

```powershell
# GUI (сборка Release + запуск в фоне)
.\run_Reader.ps1

# GUI с предустановленным символом
.\run_Reader.ps1 -Symbol CNYRUBF

# Проверка данных без GUI
.\run_Reader.ps1 -CheckDb Si
```

## Несколько окон

Приложение можно запускать сколько угодно раз — **каждый запуск открывает новое
окно**. Ограничений по экземплярам нет; окна с разными символами сосуществуют
одновременно.

### Поведение `run_Reader.ps1`

| Ситуация | Действие |
|----------|----------|
| Запущенных окон нет | Сборка решения → запуск нового окна |
| Уже есть запущенные окна | **Пересборка пропускается** → запуск нового окна из текущего билда |

Пересборка пропускается, потому что запущенные экземпляры держат файлы из
`bin\Release` (`ArtasChart.Services.dll` и др.) и сборка упала бы с `MSB3027`
(«файл занят другим процессом»). Чтобы пересобрать, закройте все окна ArtasChart
и запустите `run_Reader.ps1` снова.

`run_Reader.ps1` запускает собранный `ArtasChart.App.dll` напрямую (а не `dotnet run`),
поэтому процесс приложения не связан со сборочным конвейером. Наличие окон
определяется по `ArtasChart.App.dll` в командной строке процесса.
