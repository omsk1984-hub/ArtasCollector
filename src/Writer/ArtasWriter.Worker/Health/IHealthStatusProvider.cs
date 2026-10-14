namespace ArtasWriter.Worker.Health;

/// <summary>
/// Поставщик снапшота здоровья сервиса для эндпоинта <c>/health</c>.
/// </summary>
public interface IHealthStatusProvider
{
    /// <summary>Сформировать актуальный снапшот статуса.</summary>
    HealthSnapshot GetSnapshot();
}
