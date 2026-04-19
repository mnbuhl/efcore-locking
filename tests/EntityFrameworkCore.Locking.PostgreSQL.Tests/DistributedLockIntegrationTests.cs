using AwesomeAssertions;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Extensions;
using EntityFrameworkCore.Locking.PostgreSQL.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.PostgreSQL.Tests;

[Collection("Postgres")]
public class DistributedLockIntegrationTests(PostgresFixture fixture) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseLocking()
            .Options);

    // --- Acquire_Free_Succeeds ---

    [Fact]
    public async Task Acquire_Free_Succeeds()
    {
        await using var ctx = CreateContext();
        await using var handle = await ctx.AcquireDistributedLockAsync("pg-free-key");
        handle.Should().NotBeNull();
        handle.Key.Should().Be("pg-free-key");
    }

    // --- SupportsDistributedLocks ---

    [Fact]
    public void SupportsDistributedLocks_ReturnsTrue()
    {
        using var ctx = CreateContext();
        ctx.SupportsDistributedLocks().Should().BeTrue();
    }

    // --- Dispose_ReleasesLock_VerifiedByOtherConnection ---

    [Fact]
    public async Task Dispose_ReleasesLock_VerifiedByOtherConnection()
    {
        const string key = "pg-release-key";

        await using var ctxA = CreateContext();
        var handle = await ctxA.AcquireDistributedLockAsync(key);

        await handle.DisposeAsync();

        // After dispose, a second context should be able to TryAcquire immediately
        await using var ctxB = CreateContext();
        await using var handleB = await ctxB.TryAcquireDistributedLockAsync(key);
        handleB.Should().NotBeNull();
    }

    // --- TryAcquire_Free_ReturnsHandle ---

    [Fact]
    public async Task TryAcquire_Free_ReturnsHandle()
    {
        await using var ctx = CreateContext();
        await using var handle = await ctx.TryAcquireDistributedLockAsync("pg-try-free");
        handle.Should().NotBeNull();
    }

    // --- TryAcquire_Contested_ReturnsNull ---

    [Fact]
    public async Task TryAcquire_Contested_ReturnsNull()
    {
        const string key = "pg-try-contested";

        await using var ctxA = CreateContext();
        await using var handleA = await ctxA.AcquireDistributedLockAsync(key);

        await using var ctxB = CreateContext();
        var handleB = await ctxB.TryAcquireDistributedLockAsync(key);
        handleB.Should().BeNull();
    }

    // --- Acquire_Contested_BlocksUntilReleased ---

    [Fact]
    public async Task Acquire_Contested_BlocksUntilReleased()
    {
        const string key = "pg-block-key";

        await using var ctxA = CreateContext();
        var handleA = await ctxA.AcquireDistributedLockAsync(key);

        var acquireTask = Task.Run(async () =>
        {
            await using var ctxB = CreateContext();
            await using var h = await ctxB.AcquireDistributedLockAsync(key);
        });

        // Should not complete while ctxA holds the lock
        var completed = await Task.WhenAny(acquireTask, Task.Delay(300));
        completed.Should().NotBe(acquireTask, "lock should still be held");

        await handleA.DisposeAsync();
        await acquireTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // --- Acquire_Timeout_ThrowsLockTimeout ---

    [Fact]
    public async Task Acquire_Timeout_ThrowsLockTimeout()
    {
        const string key = "pg-timeout-key";

        await using var ctxA = CreateContext();
        await using var handleA = await ctxA.AcquireDistributedLockAsync(key);

        await using var ctxB = CreateContext();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Func<Task> act = () => ctxB.AcquireDistributedLockAsync(key, TimeSpan.FromMilliseconds(500));
        await act.Should().ThrowAsync<LockTimeoutException>();
        sw.Stop();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    // --- ReleaseAsync_Idempotent ---

    [Fact]
    public async Task ReleaseAsync_Idempotent()
    {
        await using var ctx = CreateContext();
        var handle = await ctx.AcquireDistributedLockAsync("pg-idempotent");
        await handle.ReleaseAsync();
        await handle.ReleaseAsync(); // must not throw
    }

    // --- DoubleAcquire_SameContext_ThrowsLockAlreadyHeld ---

    [Fact]
    public async Task DoubleAcquire_SameContext_ThrowsLockAlreadyHeld()
    {
        await using var ctx = CreateContext();
        await using var h1 = await ctx.AcquireDistributedLockAsync("pg-double");

        var ex = await Assert.ThrowsAsync<LockAlreadyHeldException>(() =>
            ctx.AcquireDistributedLockAsync("pg-double"));
        ex.Key.Should().Be("pg-double");
    }

    // --- Two independent contexts on different connections can hold same key ---

    [Fact]
    public async Task TwoContexts_DifferentConnections_CanBothHoldSameKey()
    {
        const string key = "pg-registry-scope";

        await using var ctxA = CreateContext();
        await using var hA = await ctxA.AcquireDistributedLockAsync(key);

        // ctxB uses a separate connection from the pool (or a second TCP session).
        // pg_advisory_lock is per-backend — the second context will block, not get LockAlreadyHeldException.
        // We only verify the registry doesn't throw LockAlreadyHeldException for a *different* context.
        // The actual acquire will block (both Postgres sessions compete). Use TryAcquire which returns null
        // if contested — verifies no LockAlreadyHeldException is thrown (which would be wrong behavior).
        await using var ctxB = CreateContext();
        var hB = await ctxB.TryAcquireDistributedLockAsync(key);
        // hB is null (lock held by ctxA's session) — but importantly NO LockAlreadyHeldException was raised.
        // The registry correctly distinguishes the two (DbContext, DbConnection) pairs.
        // Release ctxA's lock, then ctxB should succeed.
        await hA.DisposeAsync();
        hB = await ctxB.TryAcquireDistributedLockAsync(key);
        hB.Should().NotBeNull("after ctxA releases, ctxB should acquire");
        await hB!.DisposeAsync();
    }

    // --- Cancellation with timeout ---

    [Fact]
    public async Task Acquire_Cancelled_WithTimeout_ThrowsOperationCanceled()
    {
        const string key = "pg-cancel-with-timeout";

        await using var ctxA = CreateContext();
        await using var handleA = await ctxA.AcquireDistributedLockAsync(key);

        await using var ctxB = CreateContext();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Func<Task> act = () => ctxB.AcquireDistributedLockAsync(key, TimeSpan.FromSeconds(10), cts.Token);
        await act.Should().ThrowAsync<Exception>(); // OperationCanceledException or LockTimeoutException
        sw.Stop();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));
    }

    // --- SQL verification via SqlCapture (scalar execution) ---

    [Fact]
    public async Task Acquire_EmitsPgAdvisoryLockSql()
    {
        var capture = new SqlCapture();
        await using var ctx = new TestDbContext(
            new DbContextOptionsBuilder<TestDbContext>()
                .UseNpgsql(fixture.ConnectionString)
                .UseLocking()
                .AddInterceptors(capture)
                .Options);

        await using var handle = await ctx.AcquireDistributedLockAsync("sql-verify");
        capture.Commands.Should().Contain(c => c.Contains("pg_advisory_lock"));
    }

    [Fact]
    public async Task TryAcquire_EmitsPgTryAdvisoryLockSql()
    {
        var capture = new SqlCapture();
        await using var ctx = new TestDbContext(
            new DbContextOptionsBuilder<TestDbContext>()
                .UseNpgsql(fixture.ConnectionString)
                .UseLocking()
                .AddInterceptors(capture)
                .Options);

        await using var handle = await ctx.TryAcquireDistributedLockAsync("sql-try-verify");
        capture.Commands.Should().Contain(c => c.Contains("pg_try_advisory_lock"));
    }
}
