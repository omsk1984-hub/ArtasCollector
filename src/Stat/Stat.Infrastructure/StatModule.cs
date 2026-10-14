using ArtasCore.Abstractions;
using ArtasCore.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stat.Application.Abstractions;
using Stat.Application.Options;
using Stat.Infrastructure.Data;
using Stat.Infrastructure.Services;

namespace Stat.Infrastructure;

/// <summary>
/// Модуль регистрации зависимостей Stat-сервиса. Инкапсулирует композицию графа:
/// конфигурация, доступ к данным (чтение/запись stat), обработчик.
/// </summary>
public static class StatModule
{
    /// <summary>
    /// Зарегистрировать сервисы Stat в контейнере.
    /// </summary>
    public static IServiceCollection AddStatModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Секция настроек.
        services.Configure<StatOptions>(
            configuration.GetSection(StatOptions.SectionName));

        // Конфигурация БД.
        services.AddSingleton(DbConfig.FromConfiguration(configuration));

        // Абстракции доступа к данным.
        services.AddSingleton<IFractalReader, FractalReadRepository>();
        services.AddSingleton<IStatRepository, StatRepository>();

        // Batch-обработчик статистики.
        services.AddSingleton<IStatWalker, StatWalker>();

        return services;
    }
}
