using ArtasCore.Abstractions;
using Npgsql;

namespace ArtasCore.Data;

/// <summary>
/// Защита от второго экземпляра сервиса через session-level advisory-lock
/// PostgreSQL (<c>pg_try_advisory_lock</c>).
///
/// Блокировка привязана к соединению: автоматически снимается при его
/// закрытии, в том числе при аварийном завершении процесса. Соединение
/// удерживается открытым весь жизненный цикл сервиса, поэтому второй экземпляр
/// не сможет захватить тот же ключ и будет обязан завершиться.
/// </summary>
public sealed class PostgresSingleInstanceGuard : ISingleInstanceGuard
{
    /// <summary>
    /// Ключ блокировки. Произвольное фиксированное 64-битное число, уникальное
    /// для этого сервиса (не должно совпадать с ключами других сервисов БД).
    /// </summary>
    public const long WriterLockKey = 0x5772_6974_6572_01L; // "Writer" + 01

    private readonly DbConfig _config;
    private readonly long _lockKey;

    private NpgsqlConnection? _connection;
    private bool _acquired;

    /// <param name="config">Конфигурация подключения к PostgreSQL.</param>
    /// <param name="lockKey">
    /// Ключ блокировки. По умолчанию — <see cref="WriterLockKey"/>.
    /// </param>
    public PostgresSingleInstanceGuard(DbConfig config, long lockKey = WriterLockKey)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _lockKey = lockKey;
    }

    /// <inheritdoc />
    public async Task<bool> TryAcquireAsync(CancellationToken cancellationToken = default)
    {
        if (_acquired)
        {
            return true;
        }

        var connection = new NpgsqlConnection(_config.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);

            await using var cmd = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            cmd.Parameters.AddWithValue("key", _lockKey);

            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            var acquired = result is true;

            if (!acquired)
            {
                // Не удерживаем лишнее соединение, если блокировка не получена.
                await connection.DisposeAsync();
                return false;
            }

            _connection = connection;
            _acquired = true;
            return true;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            // Явно снимаем блокировку (best-effort) и закрываем соединение —
            // закрытие соединения само освобождает session-level lock.
            try
            {
                await using var cmd = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", _connection);
                cmd.Parameters.AddWithValue("key", _lockKey);
                await cmd.ExecuteScalarAsync();
            }
            catch (Exception)
            {
                // Соединение могло уже оборваться — блокировка снимется сама.
            }

            await _connection.DisposeAsync();
            _connection = null;
        }

        _acquired = false;
    }
}
