using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stat.Infrastructure;

namespace Stat.Worker;

/// <summary>
/// Модуль регистрации зависимостей Worker: делегирует в <see cref="StatModule"/>
/// и добавляет раннер batch-прогона.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Зарегистрировать все сервисы Stat + раннер.
    /// </summary>
    public static IServiceCollection AddStatWorker(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddStatModule(configuration);
        services.AddSingleton<StatRunner>();

        return services;
    }
}
