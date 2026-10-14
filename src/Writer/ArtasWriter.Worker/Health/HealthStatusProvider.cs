namespace ArtasWriter.Worker.Health;

/// <summary>
/// Тонкий адаптер <see cref="IHealthStatusProvider"/>: делегирует сбор снапшота
/// <see cref="IHealthSnapshotBuilder"/>.
/// </summary>
public sealed class HealthStatusProvider : IHealthStatusProvider
{
    private readonly IHealthSnapshotBuilder _builder;

    public HealthStatusProvider(IHealthSnapshotBuilder builder)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    }

    /// <inheritdoc />
    public HealthSnapshot GetSnapshot() => _builder.Build();
}
