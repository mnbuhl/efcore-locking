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
