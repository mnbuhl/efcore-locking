using AwesomeAssertions;
using EntityFrameworkCore.Locking.SqlServer.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.SqlServer.Tests;

[Collection("SqlServer")]
public class DistributedLockIntegrationTests(SqlServerFixture fixture) : DistributedLockIntegrationTestsBase
{
    protected override TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseSqlServer(fixture.ConnectionString).UseLocking().Options);

    // --- SqlServer-specific ---

    [Fact]
    public async Task SharedLocks_TwoContexts_CanHoldSameKeyConcurrently()
    {
        var key = $"ss-shared-concurrent-{Guid.NewGuid():N}";

        await using var ctxA = CreateContext();
        var sharedA = await ctxA.Database.AcquireDistributedLockAsync(key, mode: DistributedLockMode.Shared);

        await using var ctxB = CreateContext();
        var sharedB = await ctxB.Database.TryAcquireDistributedLockAsync(key, mode: DistributedLockMode.Shared);

        sharedB.Should().NotBeNull();

        await sharedB!.DisposeAsync();
        await sharedA.DisposeAsync();
    }

    [Fact]
    public async Task SharedLock_BlocksExclusiveTryAcquireUntilReleased()
    {
        var key = $"ss-shared-blocks-exclusive-{Guid.NewGuid():N}";

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
        var key = $"ss-exclusive-blocks-shared-{Guid.NewGuid():N}";

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
    public async Task Acquire_Cancelled_WithTimeout_Throws()
    {
        const string key = "ss-cancel-timeout";
        await using var ctxA = CreateContext();
        await using var handleA = await ctxA.Database.AcquireDistributedLockAsync(key);

        await using var ctxB = CreateContext();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(200);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Func<Task> act = () => ctxB.Database.AcquireDistributedLockAsync(key, TimeSpan.FromSeconds(10), cts.Token);
        await act.Should().ThrowAsync<Exception>();
        sw.Stop();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));
    }
}
