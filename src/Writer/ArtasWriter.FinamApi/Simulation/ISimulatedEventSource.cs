using ArtasWriter.Application.Producers;

namespace ArtasWriter.FinamApi.Simulation;

/// <summary>
/// Источник синтетических событий Finam для режима симуляции.
/// Позволяет вынести генерацию тестовых данных из боевого продьюсера (SRP/OCP).
/// </summary>
public interface ISimulatedEventSource
{
    /// <summary>
    /// Генерировать события до отмены, передавая каждое через
    /// <paramref name="publishAsync"/>.
    /// </summary>
    /// <param name="finamSymbol">Полный символ Finam (ticker@mic).</param>
    /// <param name="publishAsync">Публикация события в канал продьюсера.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task RunAsync(
        string finamSymbol,
        Func<FinamEvent, bool> publishAsync,
        CancellationToken cancellationToken);
}
