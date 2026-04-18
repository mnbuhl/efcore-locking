using AwesomeAssertions;
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
        lockedRow.Should().NotBeNull();

        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();
        var skipped = await ctxB.Products.Where(p => p.Id == id)
            .ForUpdate(LockBehavior.SkipLocked)
            .FirstOrDefaultAsync();

        skipped.Should().BeNull();

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

        Func<Task> act = async () => await ctxB.Products.Where(p => p.Id == id)
            .ForUpdate(LockBehavior.NoWait)
            .FirstOrDefaultAsync();

        await act.Should().ThrowAsync<LockTimeoutException>();

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
        Func<Task> act = async () => await ctxB.Products.Where(p => p.Id == id)
            .ForUpdate(LockBehavior.Wait, TimeSpan.FromSeconds(1))
            .FirstOrDefaultAsync();

        await act.Should().ThrowAsync<LockTimeoutException>();
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));

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

        Func<Task> act = async () => await query.ToListAsync();
        await act.Should().ThrowAsync<LockingConfigurationException>();

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_TwoTransactions_SecondCommits_FirstSeesUpdatedData()
    {
        await using var ctxSeed = CreateContext();
        await ctxSeed.Database.EnsureCreatedAsync();
        var id = await SeedAsync(ctxSeed, "Versioned");

        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        var rowA = await ctxA.Products.Where(p => p.Id == id).ForUpdate().FirstAsync();
        rowA.Price = 99.99m;
        await ctxA.SaveChangesAsync();
        await txA.CommitAsync();

        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();
        var rowB = await ctxB.Products.Where(p => p.Id == id).ForUpdate().FirstAsync();
        rowB.Price.Should().Be(99.99m);
        await txB.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_MultipleRows_AllLocked_SkipLocked_ReturnsEmpty()
    {
        await using var ctxSeed = CreateContext();
        await ctxSeed.Database.EnsureCreatedAsync();
        var id1 = await SeedAsync(ctxSeed, "Bulk1");
        var id2 = await SeedAsync(ctxSeed, "Bulk2");

        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        var locked = await ctxA.Products
            .Where(p => p.Id == id1 || p.Id == id2)
            .ForUpdate()
            .ToListAsync();
        locked.Should().HaveCount(2);

        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();
        var skipped = await ctxB.Products
            .Where(p => p.Id == id1 || p.Id == id2)
            .ForUpdate(LockBehavior.SkipLocked)
            .ToListAsync();
        skipped.Should().BeEmpty();

        await txA.RollbackAsync();
        await txB.RollbackAsync();
    }
}
