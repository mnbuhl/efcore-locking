using AwesomeAssertions;
using EntityFrameworkCore.Locking.PostgreSQL.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.PostgreSQL.Tests;

[Collection("Postgres")]
public class DistributedLockIntegrationTests(PostgresFixture fixture) : DistributedLockIntegrationTestsBase
{
    protected override TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseNpgsql(fixture.ConnectionString).UseLocking().Options);

    // --- PG-specific ---

    [Fact]
    public async Task SharedLocks_TwoContexts_CanHoldSameKeyConcurrently()
    {
        var key = $"pg-shared-concurrent-{Guid.NewGuid():N}";

        await using var ctxA = CreateContext();
        var sharedA = await ctxA.Database.AcquireDistributedLockAsync(key, mode: DistributedLockMode.Shared);

        await using var ctxB = CreateContext();
        var sharedB = await ctxB.Database.TryAcquireDistributedLockAsync(key, mode: DistributedLockMode.Shared);

        sharedB.Should().NotBeNull();

        await sharedB!.DisposeAsync();
        await sharedA.DisposeAsync();

        await using var ctxC = CreateContext();
        var exclusive = await ctxC.Database.TryAcquireDistributedLockAsync(key);
        exclusive.Should().NotBeNull("all shared handles should release using shared unlock semantics");
        await exclusive!.DisposeAsync();
    }

    [Fact]
    public async Task SharedLock_BlocksExclusiveTryAcquireUntilReleased()
    {
        var key = $"pg-shared-blocks-exclusive-{Guid.NewGuid():N}";

        await using var ctxA = CreateContext();
        var shared = await ctxA.Database.AcquireDistributedLockAsync(key, mode: DistributedLockMode.Shared);

        await using var ctxB = CreateContext();
        var blockedExclusive = await ctxB.Database.TryAcquireDistributedLockAsync(key);
        blockedExclusive.Should().BeNull();

        await shared.DisposeAsync();

        var exclusive = await ctxB.Database.TryAcquireDistributedLockAsync(key);
        exclusive.Should().NotBeNull();
        await exclusive!.DisposeAsync();
    }

    [Fact]
    public async Task ExclusiveLock_BlocksSharedTryAcquireUntilReleased()
    {
        var key = $"pg-exclusive-blocks-shared-{Guid.NewGuid():N}";

        await using var ctxA = CreateContext();
        var exclusive = await ctxA.Database.AcquireDistributedLockAsync(key);

        await using var ctxB = CreateContext();
        var blockedShared = await ctxB.Database.TryAcquireDistributedLockAsync(key, mode: DistributedLockMode.Shared);
        blockedShared.Should().BeNull();

        await exclusive.DisposeAsync();

        var shared = await ctxB.Database.TryAcquireDistributedLockAsync(key, mode: DistributedLockMode.Shared);
        shared.Should().NotBeNull();
        await shared!.DisposeAsync();
    }

    [Fact]
    public async Task Acquire_Contested_BlocksUntilReleased()
    {
        const string key = "pg-block-key";

        await using var ctxA = CreateContext();
        var handleA = await ctxA.Database.AcquireDistributedLockAsync(key);

        var acquireTask = Task.Run(async () =>
        {
            await using var ctxB = CreateContext();
            await using var h = await ctxB.Database.AcquireDistributedLockAsync(key);
        });

        var completed = await Task.WhenAny(acquireTask, Task.Delay(300));
        completed.Should().NotBe(acquireTask, "lock should still be held");

        await handleA.DisposeAsync();
        await acquireTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TwoContexts_DifferentConnections_CanBothHoldSameKey()
    {
        const string key = "pg-registry-scope";

        await using var ctxA = CreateContext();
        await using var hA = await ctxA.Database.AcquireDistributedLockAsync(key);

        await using var ctxB = CreateContext();
        var hB = await ctxB.Database.TryAcquireDistributedLockAsync(key);
        await hA.DisposeAsync();
        hB = await ctxB.Database.TryAcquireDistributedLockAsync(key);
        hB.Should().NotBeNull("after ctxA releases, ctxB should acquire");
        await hB!.DisposeAsync();
    }

    [Fact]
    public async Task Acquire_Cancelled_WithTimeout_ThrowsOperationCanceled()
    {
        const string key = "pg-cancel-with-timeout";

        await using var ctxA = CreateContext();
        await using var handleA = await ctxA.Database.AcquireDistributedLockAsync(key);

        await using var ctxB = CreateContext();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Func<Task> act = () => ctxB.Database.AcquireDistributedLockAsync(key, TimeSpan.FromSeconds(10), cts.Token);
        await act.Should().ThrowAsync<Exception>();
        sw.Stop();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));
    }
}
