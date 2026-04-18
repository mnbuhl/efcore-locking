using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.PostgreSQL.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.PostgreSQL.Tests;

[Collection("Postgres")]
public class ConcurrencyTests(PostgresFixture fixture)
{
    private TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseLocking()
            .Options);

    private static async Task<int> SeedAsync(TestDbContext ctx, string name = "Widget")
    {
        var p = new Product { Name = name, Price = 9.99m };
        ctx.Products.Add(p);
        await ctx.SaveChangesAsync();
        return p.Id;
    }

    [Fact]
    public async Task ForUpdate_SkipLocked_SecondTransaction_SkipsLockedRow()
    {
        await using var ctxSeed = CreateContext();
        await ctxSeed.Database.EnsureCreatedAsync();
        var id = await SeedAsync(ctxSeed);

        // Transaction A acquires lock
        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        var lockedRow = await ctxA.Products.Where(p => p.Id == id).ForUpdate().FirstOrDefaultAsync();
        Assert.NotNull(lockedRow);

        // Transaction B with SKIP LOCKED should get null (row skipped)
        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();
        var skipped = await ctxB.Products.Where(p => p.Id == id)
            .ForUpdate(LockBehavior.SkipLocked)
            .FirstOrDefaultAsync();

        Assert.Null(skipped);

        await txA.RollbackAsync();
        await txB.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_NoWait_SecondTransaction_ThrowsLockTimeoutException()
    {
        await using var ctxSeed = CreateContext();
        await ctxSeed.Database.EnsureCreatedAsync();
        var id = await SeedAsync(ctxSeed, "NoWait Widget");

        // Transaction A holds the lock
        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        await ctxA.Products.Where(p => p.Id == id).ForUpdate().FirstOrDefaultAsync();

        // Transaction B with NOWAIT should throw LockTimeoutException immediately
        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();

        await Assert.ThrowsAsync<LockTimeoutException>(() =>
            ctxB.Products.Where(p => p.Id == id)
                .ForUpdate(LockBehavior.NoWait)
                .FirstOrDefaultAsync());

        await txA.RollbackAsync();
        await txB.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_WaitTimeout_SecondTransaction_ThrowsLockTimeoutExceptionWithinTimeout()
    {
        await using var ctxSeed = CreateContext();
        await ctxSeed.Database.EnsureCreatedAsync();
        var id = await SeedAsync(ctxSeed, "Timeout Widget");

        // Transaction A holds the lock
        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        await ctxA.Products.Where(p => p.Id == id).ForUpdate().FirstOrDefaultAsync();

        // Transaction B with 200ms timeout
        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<LockTimeoutException>(() =>
            ctxB.Products.Where(p => p.Id == id)
                .ForUpdate(LockBehavior.Wait, TimeSpan.FromMilliseconds(200))
                .FirstOrDefaultAsync());
        sw.Stop();

        // Should fail well under 3 seconds
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3),
            $"Lock timeout took too long: {sw.Elapsed}");

        await txA.RollbackAsync();
        await txB.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_UnionQuery_ThrowsLockingConfigurationException()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        var query = ctx.Products.Where(p => p.Id == 1)
            .Union(ctx.Products.Where(p => p.Id == 2))
            .ForUpdate();

        await Assert.ThrowsAsync<LockingConfigurationException>(() =>
            query.ToListAsync());

        await tx.RollbackAsync();
    }
}
