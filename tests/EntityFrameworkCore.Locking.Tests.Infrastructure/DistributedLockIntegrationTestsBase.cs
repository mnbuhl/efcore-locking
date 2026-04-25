using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using Xunit;

namespace EntityFrameworkCore.Locking.Tests.Infrastructure;

public abstract class DistributedLockIntegrationTestsBase : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    protected abstract TestDbContext CreateContext();

    /// <summary>
    /// Timeout for <see cref="Acquire_Timeout_ThrowsLockTimeout"/>. Default 500ms.
    /// MySQL overrides to 1s because GET_LOCK timeout is integer-second granularity.
    /// </summary>
    protected virtual TimeSpan DistributedLockAcquireTimeout => TimeSpan.FromMilliseconds(500);

    [Fact]
    public async Task Acquire_Free_Succeeds()
    {
        await using var ctx = CreateContext();
        await using var handle = await ctx.Database.AcquireDistributedLockAsync("free-key");
        handle.Should().NotBeNull();
        handle.Key.Should().Be("free-key");
    }

    [Fact]
    public void SupportsDistributedLocks_ReturnsTrue()
    {
        using var ctx = CreateContext();
        ctx.Database.SupportsDistributedLocks().Should().BeTrue();
    }

    [Fact]
    public async Task Dispose_ReleasesLock_VerifiedByOtherConnection()
    {
        const string key = "release-key";

        await using var ctxA = CreateContext();
        var handle = await ctxA.Database.AcquireDistributedLockAsync(key);

        await handle.DisposeAsync();

        await using var ctxB = CreateContext();
        await using var handleB = await ctxB.Database.TryAcquireDistributedLockAsync(key);
        handleB.Should().NotBeNull();
    }

    [Fact]
    public async Task TryAcquire_Free_ReturnsHandle()
    {
        await using var ctx = CreateContext();
        await using var handle = await ctx.Database.TryAcquireDistributedLockAsync("try-free");
        handle.Should().NotBeNull();
    }

    [Fact]
    public async Task TryAcquire_Contested_ReturnsNull()
    {
        const string key = "try-contested";

        await using var ctxA = CreateContext();
        await using var handleA = await ctxA.Database.AcquireDistributedLockAsync(key);

        await using var ctxB = CreateContext();
        var handleB = await ctxB.Database.TryAcquireDistributedLockAsync(key);
        handleB.Should().BeNull();
    }

    [Fact]
    public async Task Acquire_Timeout_ThrowsLockTimeout()
    {
        const string key = "timeout-key";

        await using var ctxA = CreateContext();
        await using var handleA = await ctxA.Database.AcquireDistributedLockAsync(key);

        await using var ctxB = CreateContext();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Func<Task> act = () => ctxB.Database.AcquireDistributedLockAsync(key, DistributedLockAcquireTimeout);
        await act.Should().ThrowAsync<LockTimeoutException>();
        sw.Stop();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ReleaseAsync_Idempotent()
    {
        await using var ctx = CreateContext();
        var handle = await ctx.Database.AcquireDistributedLockAsync("idempotent");
        await handle.ReleaseAsync();
        await handle.ReleaseAsync(); // must not throw
    }

    [Fact]
    public async Task DoubleAcquire_SameContext_ThrowsLockAlreadyHeld()
    {
        await using var ctx = CreateContext();
        await using var h1 = await ctx.Database.AcquireDistributedLockAsync("double");

        var ex = await Assert.ThrowsAsync<LockAlreadyHeldException>(() =>
            ctx.Database.AcquireDistributedLockAsync("double")
        );
        ex.Key.Should().Be("double");
    }
}
