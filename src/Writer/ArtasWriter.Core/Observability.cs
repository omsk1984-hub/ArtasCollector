using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Instrumentation.Runtime;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ArtasWriter.Core;

/// <summary>Уровень события метрики (используется внешними хуками).</summary>
public enum Severity { Error, Warning, Info, Debug }

/// <summary>Статус завершения трейса.</summary>
public enum SpanStatus { Ok, Error }

/// <summary>
/// Трейс (span) одной операции. Обёртка над <see cref="Activity"/> OpenTelemetry:
/// сохраняет прежний fluent-API (SetTag/SetStatus/RecordException/End), чтобы
/// вызывающий код не менялся.
/// </summary>
public sealed class Span
{
    private readonly Activity? _activity;

    internal Span(Activity? activity) { _activity = activity; }

    /// <summary>Добавить строковый тег.</summary>
    public Span SetTag(string key, string value)
    {
        _activity?.SetTag(key, value);
        return this;
    }

    /// <summary>Добавить целочисленный тег.</summary>
    public Span SetTag(string key, int value)
    {
        _activity?.SetTag(key, value);
        return this;
    }

    /// <summary>Добавить long-тег.</summary>
    public Span SetTag(string key, long value)
    {
        _activity?.SetTag(key, value);
        return this;
    }

    /// <summary>Установить статус завершения трейса.</summary>
    public Span SetStatus(SpanStatus status)
    {
        _activity?.SetStatus(status == SpanStatus.Error
            ? ActivityStatusCode.Error
            : ActivityStatusCode.Ok);
        return this;
    }

    /// <summary>Записать исключение в трейс.</summary>
    public Span RecordException(Exception ex)
    {
        // OTel-конвенция: теги exception.* + статус Error.
        _activity?.SetTag("exception.type", ex.GetType().FullName);
        _activity?.SetTag("exception.message", ex.Message);
        _activity?.SetTag("exception.stacktrace", ex.ToString());
        _activity?.SetStatus(ActivityStatusCode.Error);
        return this;
    }

    /// <summary>Завершить трейс.</summary>
    public void End() => _activity?.Dispose();
}

/// <summary>
/// Метрика (счётчик или гистограмма). Обёртка над инструментами OpenTelemetry
/// (<see cref="Counter{T}"/> / <see cref="Histogram{T}"/>); сохраняет прежний
/// API (Add/Record). Если SDK не инициализирован — операции безопасны (no-op).
/// </summary>
public sealed class Meter
{
    private readonly Counter<long>? _counter;
    private readonly Histogram<double>? _histogram;
    private readonly string _name;

    private Meter(string name, Counter<long>? counter, Histogram<double>? histogram)
    {
        _name = name;
        _counter = counter;
        _histogram = histogram;
    }

    internal static Meter CreateCounter(string name) =>
        new(name, Observability.GetCounter(name), null);

    internal static Meter CreateHistogram(string name) =>
        new(name, null, Observability.GetHistogram(name));

    /// <summary>Инкремент счётчика.</summary>
    public void Add(long value)
    {
        _counter?.Add(value);
        Observability.RaiseMetric(_name, value, Severity.Info);
    }

    /// <summary>Инкремент счётчика.</summary>
    public void Add(int value) => Add((long)value);

    /// <summary>Запись значения в гистограмму.</summary>
    public void Record(double value)
    {
        _histogram?.Record(value);
        Observability.RaiseMetric(_name, (long)(value * 1000), Severity.Info);
    }
}

/// <summary>
/// Центральная точка observability: трейсы и метрики поверх OpenTelemetry SDK.
///
/// Семантика:
///  • До вызова <see cref="Initialize"/> — безопасный no-op (Widgets не создаются).
///  • После <see cref="Initialize"/> — метрики и трейсы уходят в OTLP-коллектор
///    (gRPC, по умолчанию <c>http://localhost:4317</c>).
///
/// Пакеты OpenTelemetry версии 1.17.0 (lib/net10.0) подключены в
/// <c>ArtasWriter.Core.csproj</c>.
/// </summary>
public sealed class Observability
{
    private Observability() { } // static only

    /// <summary>Имя логического источника (используется как scope для Meter/ActivitySource).</summary>
    public const string ScopeName = "ArtasWriter";

    private static readonly object SyncRoot = new();
    private static volatile bool _initialized;

    private static System.Diagnostics.Metrics.Meter? _meter;
    private static ActivitySource? _activitySource;
    private static MeterProvider? _meterProvider;
    private static TracerProvider? _tracerProvider;

    // Кэш инструментов: инкрементальные Counter/Histogram переиспользуются.
    private static readonly ConcurrentDictionary<string, Counter<long>> Counters = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Histogram<double>> Histograms = new(StringComparer.Ordinal);

    // region: Инициализация SDK

    /// <summary>
    /// Инициализировать OpenTelemetry SDK (безопасно вызывать многократно).
    /// </summary>
    /// <param name="otlpEndpoint">
    /// OTLP gRPC endpoint коллектора. По умолчанию —
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> или <c>http://localhost:4317</c>.
    /// </param>
    public static void Initialize(string? otlpEndpoint = null)
    {
        if (_initialized) return;

        lock (SyncRoot)
        {
            if (_initialized) return;

            var endpoint = otlpEndpoint
                ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
                ?? "http://localhost:4317";

            var resource = ResourceBuilder.CreateDefault()
                .AddService(serviceName: "artas-writer", serviceVersion: "1.0.0")
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] =
                        Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "production",
                });

            _meter = new System.Diagnostics.Metrics.Meter(ScopeName);
            _activitySource = new ActivitySource(ScopeName);

            // --- Metrics ---
            _meterProvider = Sdk.CreateMeterProviderBuilder()
                .SetResourceBuilder(resource)
                .AddMeter(ScopeName)
                // Runtime-метрики .NET (System.Runtime Meter): dotnet.gc.*
                // (сборки, время GC, размеры поколений/кучи), dotnet.jit.*,
                // dotnet.thread_pool.*. Позволяет отслеживать нагрузку на GC
                // и рост управляемой памяти (кандидат на утечку).
                .AddRuntimeInstrumentation()
                .AddOtlpExporter(o =>
                {
                    o.Endpoint = new Uri(endpoint);
                    o.Protocol = OtlpExportProtocol.Grpc;
                })
                .Build();

            // --- Traces ---
            _tracerProvider = Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(resource)
                .AddSource(ScopeName)
                .AddOtlpExporter(o =>
                {
                    o.Endpoint = new Uri(endpoint);
                    o.Protocol = OtlpExportProtocol.Grpc;
                })
                .Build();

            _initialized = true;
        }
    }

    /// <summary>
    /// Корректно освободить провайдеры (flush невыгруженных метрик/трейсов).
    /// Вызывать при остановке приложения.
    /// </summary>
    public static void Shutdown()
    {
        lock (SyncRoot)
        {
            if (!_initialized) return;

            _tracerProvider?.Dispose();
            _meterProvider?.Dispose();
            _activitySource?.Dispose();
            _meter?.Dispose();

            _tracerProvider = null;
            _meterProvider = null;
            _activitySource = null;
            _meter = null;
            Counters.Clear();
            Histograms.Clear();
            _initialized = false;
        }
    }

    // endregion

    // region: Фабрики инструментов

    /// <summary>Создать счётчик (монотонно возрастающее значение).</summary>
    public static Meter Counter(string name) => Meter.CreateCounter(name);

    /// <summary>Создать гистограмму (замеры времени/размера).</summary>
    public static Meter Histogram(string name) => Meter.CreateHistogram(name);

    // endregion

    // region: Трейсинг

    /// <summary>Начать трейс (span) для именованной операции.</summary>
    public static Span StartSpan(string name)
    {
        var source = _activitySource;
        // ActivitySource.StartActivity вернёт null, если трейсинг отключён
        // (SDK не инициализирован) — Span корректно работает как no-op.
        return new Span(source?.StartActivity(name));
    }

    // endregion

    // region: Внутренние инструменты OTel

    internal static Counter<long>? GetCounter(string name)
    {
        var meter = _meter;
        if (meter is null) return null;
        return Counters.GetOrAdd(name, n => meter.CreateCounter<long>(n));
    }

    internal static Histogram<double>? GetHistogram(string name)
    {
        var meter = _meter;
        if (meter is null) return null;
        return Histograms.GetOrAdd(name, n => meter.CreateHistogram<double>(n));
    }

    // endregion

    // region: Hook для внешнего наблюдателя (например, Loki / логирование)

    /// <summary>
    /// Коллбэк, вызываемый при каждом метрическом событии (независимо от OTel SDK).
    /// По умолчанию — игнорирует. Можно подменить на отправку в Loki/лог.
    /// </summary>
    public static Action<string, long, Severity>? MetricHandler;

    /// <summary>
    /// Вызвать внешний наблюдатель метрик. Вызывается из <see cref="Meter.Add"/> /
    /// <see cref="Meter.Record"/>.
    /// </summary>
    internal static void RaiseMetric(string name, long value, Severity severity)
    {
        var handler = MetricHandler;
        if (handler is not null)
        {
            handler(name, value, severity);
        }
    }

    // endregion
}
