using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.MySql.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.MySql.Tests;

[Collection("MySql")]
public class IntegrationTests(MySqlFixture fixture)
{
    private TestDbContext CreateContext()
    {
        var serverVersion = ServerVersion.AutoDetect(fixture.ConnectionString);
        return new(new DbContextOptionsBuilder<TestDbContext>()
            .UseMySql(fixture.ConnectionString, serverVersion)
            .UseLocking()
            .Options);
    }

    private async Task<int> SeedProductAsync(TestDbContext ctx, string name = "Widget", decimal price = 9.99m)
    {
        var p = new Product { Name = name, Price = price };
        ctx.Products.Add(p);
        await ctx.SaveChangesAsync();
        return p.Id;
    }

    [Fact]
    public async Task ForUpdate_WithTransaction_ExecutesSuccessfully()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        var id = await SeedProductAsync(ctx);

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var row = await ctx.Products.Where(x => x.Id == id).ForUpdate().FirstOrDefaultAsync();

        row.Should().NotBeNull();
        row!.Id.Should().Be(id);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_WithoutTransaction_ThrowsInvalidOperationException()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();

        Func<Task> act = async () => await ctx.Products.Where(x => x.Id == 1).ForUpdate().FirstOrDefaultAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ForUpdate_GeneratesExactSql()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        var sql = ctx.Products.Where(x => x.Id == 1).ForUpdate().ToQueryString();

        sql.Should().Contain("FOR UPDATE");
        sql.Should().NotContain("NOWAIT");
        sql.Should().NotContain("SKIP LOCKED");
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_NoWait_GeneratesExactSql()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        var sql = ctx.Products.Where(x => x.Id == 1).ForUpdate(LockBehavior.NoWait).ToQueryString();

        sql.Should().Contain("FOR UPDATE NOWAIT");
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_SkipLocked_GeneratesExactSql()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        var sql = ctx.Products.Where(x => x.Id == 1).ForUpdate(LockBehavior.SkipLocked).ToQueryString();

        sql.Should().Contain("FOR UPDATE SKIP LOCKED");
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForShare_GeneratesExactSql()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        var sql = ctx.Products.Where(x => x.Id == 1).ForShare().ToQueryString();

        sql.Should().Contain("FOR SHARE");
        sql.Should().NotContain("FOR UPDATE");
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForShare_WithTransaction_ReturnsData()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        var id = await SeedProductAsync(ctx, "Share Me");

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var row = await ctx.Products.Where(x => x.Id == id).ForShare().FirstOrDefaultAsync();

        row.Should().NotBeNull();
        row!.Name.Should().Be("Share Me");
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_ToList_LocksMultipleRows()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        await SeedProductAsync(ctx, "MultiA");
        await SeedProductAsync(ctx, "MultiB");

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var products = await ctx.Products
            .Where(p => p.Name == "MultiA" || p.Name == "MultiB")
            .ForUpdate()
            .ToListAsync();

        products.Should().HaveCount(2);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_NonExistentRow_ReturnsNull()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var row = await ctx.Products.Where(p => p.Id == int.MaxValue).ForUpdate().FirstOrDefaultAsync();

        row.Should().BeNull();
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task SaveChanges_WithoutLock_WorksNormally()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();

        var product = new Product { Name = "NoLock", Price = 1.00m };
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();

        product.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ForShare_NoWait_ThrowsOnLockedRow()
    {
        await using var ctxSeed = CreateContext();
        await ctxSeed.Database.EnsureCreatedAsync();
        var id = await SeedProductAsync(ctxSeed, "ShareNoWait");

        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        await ctxA.Products.Where(p => p.Id == id).ForUpdate().FirstOrDefaultAsync();

        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();

        Func<Task> act = async () => await ctxB.Products.Where(p => p.Id == id)
            .ForShare(LockBehavior.NoWait)
            .FirstOrDefaultAsync();

        await act.Should().ThrowAsync<LockTimeoutException>();

        await txA.RollbackAsync();
        await txB.RollbackAsync();
    }
}
