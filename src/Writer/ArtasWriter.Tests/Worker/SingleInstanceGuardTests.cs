using ArtasCore.Data;
using Xunit;

namespace ArtasWriter.Tests.Worker;

/// <summary>
/// Интеграционные тесты <see cref="PostgresSingleInstanceGuard"/> (защита от
/// второго экземпляра Writer) на живой БД через session-level advisory-lock.
///
/// Проверяют ключевой инвариант: одновременно только один экземпляр может
/// удерживать блокировку Writer; после освобождения (Dispose) блокировку
/// может захватить следующий экземпляр.
///
/// Запуск: <c>dotnet test src/Writer/ArtasWriter.slnx --filter SingleInstanceGuard</c>.
/// </summary>
[Trait("Category", "Integration")]
public class SingleInstanceGuardTests
{
    // Отдельный ключ, чтобы тесты не конфликтовали с боевым WriterLockKey.
    private const long TestLockKey = 0x5772_6974_6572_FF01L;

    private static DbConfig Config => DbConfig.Default;

    [Fact]
    public async Task SecondGuard_CannotAcquire_WhileFirstHolds()
    {
        await using var first = new PostgresSingleInstanceGuard(Config, TestLockKey);
        Assert.True(await first.TryAcquireAsync(), "Первый guard должен получить блокировку.");

        await using var second = new PostgresSingleInstanceGuard(Config, TestLockKey);
        Assert.False(await second.TryAcquireAsync(), "Второй guard не должен получить ту же блокировку.");
    }

    [Fact]
    public async Task Lock_Released_AfterDispose()
    {
        await using (var first = new PostgresSingleInstanceGuard(Config, TestLockKey))
        {
            Assert.True(await first.TryAcquireAsync());
        }

        // После Dispose первого guard блокировка свободна.
        await using var second = new PostgresSingleInstanceGuard(Config, TestLockKey);
        Assert.True(await second.TryAcquireAsync(),
            "После освобождения блокировку должен получить следующий экземпляр.");
    }

    [Fact]
    public async Task TryAcquire_IsIdempotent_OnSameInstance()
    {
        await using var guard = new PostgresSingleInstanceGuard(Config, TestLockKey);
        Assert.True(await guard.TryAcquireAsync());
        // Повторный вызов на том же экземпляре — true (уже удерживаем).
        Assert.True(await guard.TryAcquireAsync());
    }
}
