using AwesomeAssertions;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Extensions;
using EntityFrameworkCore.Locking.MySql.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.MySql.Tests;

[Collection("MySql")]
public class DistributedLockIntegrationTests(MySqlFixture fixture) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseMySql(fixture.ConnectionString, ServerVersion.AutoDetect(fixture.ConnectionString))
            .UseLocking()
            .Options);

    [Fact]
    public async Task Acquire_Free_Succeeds()
    {
        await using var ctx = CreateContext();
        await using var handle = await ctx.AcquireDistributedLockAsync("mysql-free-key");
        handle.Should().NotBeNull();
        handle.Key.Should().Be("mysql-free-key");
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
        const string key = "mysql-release-key";
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
        await using var handle = await ctx.TryAcquireDistributedLockAsync("mysql-try-free");
        handle.Should().NotBeNull();
    }

    [Fact]
    public async Task TryAcquire_Contested_ReturnsNull()
    {
        const string key = "mysql-try-contested";
        await using var ctxA = CreateContext();
        await using var handleA = await ctxA.AcquireDistributedLockAsync(key);

        await using var ctxB = CreateContext();
        var handleB = await ctxB.TryAcquireDistributedLockAsync(key);
        handleB.Should().BeNull();
    }

    [Fact]
    public async Task Acquire_Timeout_ThrowsLockTimeout()
    {
        const string key = "mysql-timeout-key";
        await using var ctxA = CreateContext();
        await using var handleA = await ctxA.AcquireDistributedLockAsync(key);

        await using var ctxB = CreateContext();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // MySQL timeout granularity is seconds; use 1s minimum
        Func<Task> act = () => ctxB.AcquireDistributedLockAsync(key, TimeSpan.FromSeconds(1));
        await act.Should().ThrowAsync<LockTimeoutException>();
        sw.Stop();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ReleaseAsync_Idempotent()
    {
        await using var ctx = CreateContext();
        var handle = await ctx.AcquireDistributedLockAsync("mysql-idempotent");
        await handle.ReleaseAsync();
        await handle.ReleaseAsync();
    }

    [Fact]
    public async Task DoubleAcquire_SameContext_ThrowsLockAlreadyHeld()
    {
        await using var ctx = CreateContext();
        await using var h1 = await ctx.AcquireDistributedLockAsync("mysql-double");
        var ex = await Assert.ThrowsAsync<LockAlreadyHeldException>(() =>
            ctx.AcquireDistributedLockAsync("mysql-double"));
        ex.Key.Should().Be("mysql-double");
    }

    [Fact]
    public async Task LongKey_ExceededMysqlLimit_HashesCorrectly()
    {
        // Key > 64 chars is hashed to lock:<hex58> (64 chars total)
        var longKey = new string('x', 100);
        await using var ctx = CreateContext();
        await using var handle = await ctx.AcquireDistributedLockAsync(longKey);
        handle.Should().NotBeNull();
        handle.Key.Should().Be(longKey); // public Key is the original, not encoded
    }

    [Fact]
    public async Task Acquire_EmitsGetLockSql()
    {
        var capture = new SqlCapture();
        await using var ctx = new TestDbContext(
            new DbContextOptionsBuilder<TestDbContext>()
                .UseMySql(fixture.ConnectionString, ServerVersion.AutoDetect(fixture.ConnectionString))
                .UseLocking()
                .AddInterceptors(capture)
                .Options);

        await using var handle = await ctx.AcquireDistributedLockAsync("mysql-sql-verify");
        capture.Commands.Should().Contain(c => c.Contains("GET_LOCK"));
    }
}
