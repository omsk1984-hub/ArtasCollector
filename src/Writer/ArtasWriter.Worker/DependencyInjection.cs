using ArtasWriter.Application.Options;
using ArtasWriter.Application.Producers;
using ArtasWriter.Core;
using ArtasCore.Abstractions;
using ArtasWriter.FinamApi;
using ArtasWriter.FinamApi.Options;
using ArtasWriter.FinamApi.Simulation;
using ArtasCore.Data;
using ArtasWriter.Infrastructure.Services;
using ArtasWriter.Worker.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArtasWriter.Worker;

/// <summary>
/// Модуль регистрации зависимостей Writer. Инкапсулирует композицию графа
/// (конфигурация, абстракции ядра, транспорт Finam, writer, health, оркестрация)
/// и выбор реализаций (симуляция vs реальный клиент).
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Зарегистрировать все сервисы Writer в контейнере.
    /// </summary>
    public static IServiceCollection AddWriterModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // OpenTelemetry: инициализация SDK один раз (idempotent).
        // Endpoint коллектора: Writer:OtlpEndpoint → OTEL_EXPORTER_OTLP_ENDPOINT →
        // http://localhost:4317. При отсутствии коллектора экспорт метрик/трейсов
        // не валит приложение (OTLP-экспортёр работает асинхронно, ошибки логирует).
        var otlpEndpoint = configuration.GetValue<string?>("Writer:OtlpEndpoint");
        Observability.Initialize(otlpEndpoint);

        // Секции настроек.
        services.Configure<WriterOptions>(
            configuration.GetSection(WriterOptions.SectionName));
        services.Configure<FinamOptions>(
            configuration.GetSection(FinamOptions.SectionName));

        // Конфигурация БД.
        services.AddSingleton(DbConfig.FromConfiguration(configuration));
        services.AddSingleton(
            configuration.GetSection(FinamOptions.SectionName).Get<FinamOptions>() ?? new FinamOptions());

        // Абстракции ядра (доступ к данным).
        services.AddSingleton<ISymbolRepository, SymbolRepository>();
        // Репозиторий фракталов знает о режиме симуляции: в нём записанные
        // фракталы помечаются в колонке stat маркером 'Fake' (для поиска/удаления).
        services.AddSingleton<IFractalRepository>(sp => new FractalRepository(
            sp.GetRequiredService<DbConfig>(),
            sp.GetRequiredService<FinamOptions>().SimulationMode));
        services.AddSingleton<IHealthProbe, DbHealthProbe>();
        // Guard единственного экземпляра: session-level advisory-lock в PG.
        // Singleton — соединение блокировки живёт весь жизненный цикл host.
        services.AddSingleton<ISingleInstanceGuard>(sp =>
            new PostgresSingleInstanceGuard(sp.GetRequiredService<DbConfig>()));

        // Клиент Finam (gRPC). В режиме симуляции создаётся заглушка.
        // Единственная регистрация: контейнер владеет экземпляром и вызовет его
        // DisposeAsync ровно один раз при остановке host.
        services.AddSingleton<IFinamClient>(sp =>
        {
            var options = sp.GetRequiredService<FinamOptions>();
            if (options.SimulationMode)
            {
                return new SimulationFinamClient(sp.GetRequiredService<ILogger<SimulationFinamClient>>());
            }

            return new FinamGrpcClient(
                options,
                sp.GetRequiredService<ILogger<FinamGrpcClient>>());
        });

        // Источник синтетических событий (используется продьюсером в режиме симуляции).
        services.AddSingleton<ISimulatedEventSource, SimulationEventSource>();

        // Фабрики продьюсеров/пайплайнов Finam.
        services.AddSingleton<IFinamProducerBuilder, FinamProducerBuilder>();
        services.AddSingleton<IInstrumentPipelineFactory, InstrumentPipelineFactory>();
        services.AddSingleton<IFinamProducerFactory, FinamProducerFactory>();

        // Единственный writer-консьюмер (под абстракцией).
        services.AddSingleton<FractalWriterService>();
        services.AddSingleton<IFractalWriter>(sp => sp.GetRequiredService<FractalWriterService>());

        // Состояние и health.
        services.AddSingleton<WriterState>();
        services.AddSingleton<IWriterState>(sp => sp.GetRequiredService<WriterState>());
        services.AddSingleton<IHealthSnapshotBuilder, HealthSnapshotBuilder>();
        services.AddSingleton<IHealthStatusProvider, HealthStatusProvider>();

        // Оркестрация конвейера.
        services.AddSingleton<WriterChannelFactory>();
        services.AddSingleton<PipelineOrchestrator>();

        // Hosted service.
        services.AddHostedService<WriterHostedService>();

        return services;
    }
}
