using AwesomeAssertions;
using EntityFrameworkCore.Locking;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.SqlServer.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.SqlServer.Tests;

[Collection("SqlServer")]
public class DistributedLockIntegrationTests(SqlServerFixture fixture) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseSqlServer(fixture.ConnectionString).UseLocking().Options);

    [Fact]
    public async Task Acquire_Free_Succeeds()
    {
        await using var ctx = CreateContext();
        await using var handle = await ctx.AcquireDistributedLockAsync("ss-free-key");
        handle.Should().NotBeNull();
        handle.Key.Should().Be("ss-free-key");
    }

    [Fact]
    public void SupportsDistributedLocks_ReturnsTrue()
    {
        using var ctx = CreateContext();
        ctx.SupportsDistributedLocks().Should().BeTrue();
    }

    [Fact]
    public async Task Dispose_ReleasesLock_VerifiedByOtherConnection()
    {
        const string key = "ss-release-key";
        await using var ctxA = CreateContext();
        var handle = await ctxA.AcquireDistributedLockAsync(key);
        await handle.DisposeAsync();

        await using var ctxB = CreateContext();
        await using var handleB = await ctxB.TryAcquireDistributedLockAsync(key);
        handleB.Should().NotBeNull();
    }

    [Fact]
    public async Task TryAcquire_Free_ReturnsHandle()
    {
        await using var ctx = CreateContext();
        await using var handle = await ctx.TryAcquireDistributedLockAsync("ss-try-free");
        handle.Should().NotBeNull();
    }

    [Fact]
    public async Task TryAcquire_Contested_ReturnsNull()
    {
        const string key = "ss-try-contested";
        await using var ctxA = CreateContext();
        await using var handleA = await ctxA.AcquireDistributedLockAsync(key);

        await using var ctxB = CreateContext();
        var handleB = await ctxB.TryAcquireDistributedLockAsync(key);
        handleB.Should().BeNull();
    }

    [Fact]
    public async Task Acquire_Timeout_ThrowsLockTimeout()
    {
        const string key = "ss-timeout-key";
        await using var ctxA = CreateContext();
        await using var handleA = await ctxA.AcquireDistributedLockAsync(key);

        await using var ctxB = CreateContext();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Func<Task> act = () => ctxB.AcquireDistributedLockAsync(key, TimeSpan.FromMilliseconds(500));
        await act.Should().ThrowAsync<LockTimeoutException>();
        sw.Stop();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ReleaseAsync_Idempotent()
    {
        await using var ctx = CreateContext();
        var handle = await ctx.AcquireDistributedLockAsync("ss-idempotent");
        await handle.ReleaseAsync();
        await handle.ReleaseAsync();
    }

    [Fact]
    public async Task DoubleAcquire_SameContext_ThrowsLockAlreadyHeld()
    {
        await using var ctx = CreateContext();
        await using var h1 = await ctx.AcquireDistributedLockAsync("ss-double");
        var ex = await Assert.ThrowsAsync<LockAlreadyHeldException>(() => ctx.AcquireDistributedLockAsync("ss-double"));
        ex.Key.Should().Be("ss-double");
    }

    [Fact]
    public async Task Acquire_Cancelled_WithTimeout_Throws()
    {
        const string key = "ss-cancel-timeout";
        await using var ctxA = CreateContext();
        await using var handleA = await ctxA.AcquireDistributedLockAsync(key);

        await using var ctxB = CreateContext();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(200);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Func<Task> act = () => ctxB.AcquireDistributedLockAsync(key, TimeSpan.FromSeconds(10), cts.Token);
        await act.Should().ThrowAsync<Exception>();
        sw.Stop();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));
    }
}
