using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.MySql.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.MySql.Tests;

[Collection("MySql")]
public class ConcurrencyTests(MySqlFixture fixture)
{
    private TestDbContext CreateContext()
    {
        var serverVersion = ServerVersion.AutoDetect(fixture.ConnectionString);
        return new(new DbContextOptionsBuilder<TestDbContext>()
            .UseMySql(fixture.ConnectionString, serverVersion)
            .UseLocking()
            .Options);
    }

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

        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        var lockedRow = await ctxA.Products.Where(p => p.Id == id).ForUpdate().FirstOrDefaultAsync();
        Assert.NotNull(lockedRow);

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

        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        await ctxA.Products.Where(p => p.Id == id).ForUpdate().FirstOrDefaultAsync();

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

        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        await ctxA.Products.Where(p => p.Id == id).ForUpdate().FirstOrDefaultAsync();

        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<LockTimeoutException>(() =>
            ctxB.Products.Where(p => p.Id == id)
                .ForUpdate(LockBehavior.Wait, TimeSpan.FromSeconds(1))
                .FirstOrDefaultAsync());
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10),
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
