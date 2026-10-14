namespace ArtasWriter.Worker.Health;

/// <summary>
/// Строит снапшот здоровья сервиса из разделяемого состояния и счётчиков writer.
/// Вынесен из провайдера для разделения сборки данных и их выдачи (SRP).
/// </summary>
public interface IHealthSnapshotBuilder
{
    /// <summary>Сформировать актуальный снапшот статуса.</summary>
    HealthSnapshot Build();
}
