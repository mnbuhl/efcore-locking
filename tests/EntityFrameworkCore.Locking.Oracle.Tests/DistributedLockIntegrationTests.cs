using AwesomeAssertions;
using EntityFrameworkCore.Locking.Oracle.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.Oracle.Tests;

[Collection("Oracle")]
public class DistributedLockIntegrationTests(OracleFixture fixture) : DistributedLockIntegrationTestsBase
{
    protected override TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseOracle(fixture.ConnectionString).UseLocking().Options);

    // DBMS_LOCK.REQUEST timeout is integer-second granularity.
    protected override TimeSpan DistributedLockAcquireTimeout => TimeSpan.FromSeconds(1);

    /// <summary>
    /// Regression: DBMS_LOCK.ALLOCATE_UNIQUE performs an implicit commit. We use the integer-id
    /// overload of DBMS_LOCK.REQUEST/RELEASE to avoid that, so acquiring an advisory lock inside
    /// an open EF transaction must leave pending DML rollback-able.
    /// </summary>
    [Fact]
    public async Task Acquire_InsideOpenTransaction_DoesNotCommitPendingDml()
    {
        await using var ctx = CreateContext();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        var category = new Category { Name = "PendingRollback" };
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();

        await using (await ctx.Database.AcquireDistributedLockAsync("tx-neutral-key"))
        {
            // Lock acquired while transaction is still open with uncommitted INSERT.
        }

        await tx.RollbackAsync();

        await using var verifyCtx = CreateContext();
        (await verifyCtx.Categories.CountAsync(c => c.Name == "PendingRollback"))
            .Should()
            .Be(0, "rollback must undo the INSERT — DBMS_LOCK.REQUEST must not have committed");
    }
}
