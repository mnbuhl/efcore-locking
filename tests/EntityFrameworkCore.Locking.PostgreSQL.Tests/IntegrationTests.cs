using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.PostgreSQL.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.PostgreSQL.Tests;

[Collection("Postgres")]
public class IntegrationTests(PostgresFixture fixture)
{
    private TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseLocking()
            .Options);

    private async Task<int> SeedProductAsync(TestDbContext ctx, string name = "Widget", decimal price = 9.99m)
    {
        var product = new Product { Name = name, Price = price };
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();
        return product.Id;
    }

    [Fact]
    public async Task ForUpdate_WithTransaction_ExecutesSuccessfully()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        var id = await SeedProductAsync(ctx);

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var product = await ctx.Products
            .Where(p => p.Id == id)
            .ForUpdate()
            .FirstOrDefaultAsync();

        product.Should().NotBeNull();
        product!.Id.Should().Be(id);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_WithoutTransaction_ThrowsInvalidOperationException()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        var id = await SeedProductAsync(ctx);

        Func<Task> act = async () => await ctx.Products
            .Where(p => p.Id == id)
            .ForUpdate()
            .FirstOrDefaultAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ForUpdate_GeneratesExactSql()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();

        var sql = ctx.Products.Where(p => p.Id == 1).ForUpdate().ToQueryString();

        sql.Should().Contain("FOR UPDATE");
        sql.Should().NotContain("FOR SHARE");
        sql.Should().NotContain("NOWAIT");
        sql.Should().NotContain("SKIP LOCKED");
    }

    [Fact]
    public async Task ForUpdate_NoWait_GeneratesExactSql()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();

        var sql = ctx.Products.Where(p => p.Id == 1).ForUpdate(LockBehavior.NoWait).ToQueryString();

        sql.Should().Contain("FOR UPDATE NOWAIT");
    }

    [Fact]
    public async Task ForUpdate_SkipLocked_GeneratesExactSql()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();

        var sql = ctx.Products.Where(p => p.Id == 1).ForUpdate(LockBehavior.SkipLocked).ToQueryString();

        sql.Should().Contain("FOR UPDATE SKIP LOCKED");
    }

    [Fact]
    public async Task ForShare_GeneratesExactSql()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();

        var sql = ctx.Products.Where(p => p.Id == 1).ForShare().ToQueryString();

        sql.Should().Contain("FOR SHARE");
        sql.Should().NotContain("FOR UPDATE");
    }

    [Fact]
    public async Task ForShare_NoWait_GeneratesExactSql()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();

        var sql = ctx.Products.Where(p => p.Id == 1).ForShare(LockBehavior.NoWait).ToQueryString();

        sql.Should().Contain("FOR SHARE NOWAIT");
    }

    [Fact]
    public async Task ForShare_SkipLocked_GeneratesExactSql()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();

        var sql = ctx.Products.Where(p => p.Id == 1).ForShare(LockBehavior.SkipLocked).ToQueryString();

        sql.Should().Contain("FOR SHARE SKIP LOCKED");
    }

    [Fact]
    public async Task ForUpdate_WithTimeout_InjectsSqlBeforeQuery()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        var id = await SeedProductAsync(ctx, "Timeout Safe");

        await using var ctx2 = CreateContext();
        await using var tx2 = await ctx2.Database.BeginTransactionAsync();

        var row = await ctx2.Products.Where(p => p.Id == id)
            .ForUpdate(LockBehavior.Wait, TimeSpan.FromMilliseconds(500))
            .FirstOrDefaultAsync();

        row.Should().NotBeNull();
        await tx2.RollbackAsync();
    }

    [Fact]
    public async Task ForShare_WithTransaction_ReturnsData()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        var id = await SeedProductAsync(ctx, "Share Me");

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var product = await ctx.Products
            .Where(p => p.Id == id)
            .ForShare()
            .FirstOrDefaultAsync();

        product.Should().NotBeNull();
        product!.Name.Should().Be("Share Me");
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_ToList_LocksMultipleRows()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        await SeedProductAsync(ctx, "Multi1");
        await SeedProductAsync(ctx, "Multi2");

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var products = await ctx.Products
            .Where(p => p.Name == "Multi1" || p.Name == "Multi2")
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
        var product = await ctx.Products
            .Where(p => p.Id == int.MaxValue)
            .ForUpdate()
            .FirstOrDefaultAsync();

        product.Should().BeNull();
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
    public async Task ForUpdate_UnsupportedShape_Except_ThrowsLockingConfigurationException()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        var query = ctx.Products.Where(p => p.Id == 1)
            .Except(ctx.Products.Where(p => p.Id == 2))
            .ForUpdate();

        Func<Task> act = async () => await query.ToListAsync();
        await act.Should().ThrowAsync<LockingConfigurationException>();

        await tx.RollbackAsync();
    }
}
