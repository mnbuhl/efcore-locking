using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.MySql.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.MySql.Tests;

[Collection("MySql")]
public class ConcurrencyTests(MySqlFixture fixture) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `OrderLines`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `Products`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `Categories`");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private TestDbContext CreateContext()
    {
        var serverVersion = ServerVersion.AutoDetect(fixture.ConnectionString);
        return new(
            new DbContextOptionsBuilder<TestDbContext>()
                .UseMySql(fixture.ConnectionString, serverVersion)
                .UseLocking()
                .Options
        );
    }

    private static async Task<int> SeedAsync(TestDbContext ctx, string name = "Widget")
    {
        var cat = new Category { Name = "Default" };
        ctx.Categories.Add(cat);
        await ctx.SaveChangesAsync();
        var p = new Product
        {
            Name = name,
            Price = 9.99m,
            Stock = 10,
            CategoryId = cat.Id,
        };
        ctx.Products.Add(p);
        await ctx.SaveChangesAsync();
        return p.Id;
    }

    [Fact]
    public async Task ForUpdate_SkipLocked_SecondTransaction_SkipsLockedRow()
    {
        await using var ctxSeed = CreateContext();
        var id = await SeedAsync(ctxSeed);

        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        (await ctxA.Products.Where(p => p.Id == id).ForUpdate().FirstOrDefaultAsync()).Should().NotBeNull();

        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();
        (await ctxB.Products.Where(p => p.Id == id).ForUpdate(LockBehavior.SkipLocked).FirstOrDefaultAsync())
            .Should()
            .BeNull();

        await txA.RollbackAsync();
        await txB.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_SkipLocked_MultipleRows_ReturnsOnlyUnlockedRows()
    {
        await using var ctxSeed = CreateContext();
        var id1 = await SeedAsync(ctxSeed, "Locked");
        var id2 = await SeedAsync(ctxSeed, "Free");

        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        await ctxA.Products.Where(p => p.Id == id1).ForUpdate().FirstOrDefaultAsync();

        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();
        var results = await ctxB
            .Products.Where(p => p.Id == id1 || p.Id == id2)
            .ForUpdate(LockBehavior.SkipLocked)
            .ToListAsync();

        results.Should().HaveCount(1);
        results[0].Id.Should().Be(id2);

        await txA.RollbackAsync();
        await txB.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_MultipleRows_AllLocked_SkipLocked_ReturnsEmpty()
    {
        await using var ctxSeed = CreateContext();
        var id1 = await SeedAsync(ctxSeed, "Bulk1");
        var id2 = await SeedAsync(ctxSeed, "Bulk2");

        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        (await ctxA.Products.Where(p => p.Id == id1 || p.Id == id2).ForUpdate().ToListAsync()).Should().HaveCount(2);

        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();
        (await ctxB.Products.Where(p => p.Id == id1 || p.Id == id2).ForUpdate(LockBehavior.SkipLocked).ToListAsync())
            .Should()
            .BeEmpty();

        await txA.RollbackAsync();
        await txB.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_NoWait_SecondTransaction_ThrowsLockTimeoutException()
    {
        await using var ctxSeed = CreateContext();
        var id = await SeedAsync(ctxSeed, "NoWait Widget");

        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        await ctxA.Products.Where(p => p.Id == id).ForUpdate().FirstOrDefaultAsync();

        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();
        Func<Task> act = async () =>
            await ctxB.Products.Where(p => p.Id == id).ForUpdate(LockBehavior.NoWait).FirstOrDefaultAsync();

        await act.Should().ThrowAsync<LockTimeoutException>();
        await txA.RollbackAsync();
        await txB.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_WaitTimeout_SecondTransaction_ThrowsLockTimeoutExceptionWithinTimeout()
    {
        await using var ctxSeed = CreateContext();
        var id = await SeedAsync(ctxSeed, "Timeout Widget");

        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        await ctxA.Products.Where(p => p.Id == id).ForUpdate().FirstOrDefaultAsync();

        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Func<Task> act = async () =>
            await ctxB
                .Products.Where(p => p.Id == id)
                .ForUpdate(LockBehavior.Wait, TimeSpan.FromSeconds(1))
                .FirstOrDefaultAsync();

        await act.Should().ThrowAsync<LockTimeoutException>();
        sw.Stop();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));

        await txA.RollbackAsync();
        await txB.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_AfterRollback_LockIsReleased_SecondTransactionSucceedsWithNoWait()
    {
        await using var ctxSeed = CreateContext();
        var id = await SeedAsync(ctxSeed, "Rollback Release");

        await using var ctxA = CreateContext();
        var txA = await ctxA.Database.BeginTransactionAsync();
        await ctxA.Products.Where(p => p.Id == id).ForUpdate().FirstOrDefaultAsync();
        await txA.RollbackAsync();
        await txA.DisposeAsync();

        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();
        var row = await ctxB.Products.Where(p => p.Id == id).ForUpdate(LockBehavior.NoWait).FirstOrDefaultAsync();

        row.Should().NotBeNull();
        await txB.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_TwoTransactions_SecondCommits_FirstSeesUpdatedData()
    {
        await using var ctxSeed = CreateContext();
        var id = await SeedAsync(ctxSeed, "Versioned");

        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        var rowA = await ctxA.Products.Where(p => p.Id == id).ForUpdate().FirstAsync();
        rowA.Price = 99.99m;
        await ctxA.SaveChangesAsync();
        await txA.CommitAsync();

        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();
        (await ctxB.Products.Where(p => p.Id == id).ForUpdate().FirstAsync()).Price.Should().Be(99.99m);
        await txB.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_UnionQuery_ThrowsLockingConfigurationException()
    {
        await using var ctx = CreateContext();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        Func<Task> act = async () =>
            await ctx
                .Products.Where(p => p.Id == 1)
                .Union(ctx.Products.Where(p => p.Id == 2))
                .ForUpdate()
                .ToListAsync();

        await act.Should().ThrowAsync<LockingConfigurationException>();
        await tx.RollbackAsync();
    }
}
