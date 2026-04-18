using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.SqlServer.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.SqlServer.Tests;

[Collection("SqlServer")]
public class ConcurrencyTests(SqlServerFixture fixture)
{
    private TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer(fixture.ConnectionString)
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
                .ForUpdate(LockBehavior.Wait, TimeSpan.FromMilliseconds(500))
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
