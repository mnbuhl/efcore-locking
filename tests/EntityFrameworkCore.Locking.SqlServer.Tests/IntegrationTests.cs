using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.SqlServer.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.SqlServer.Tests;

[Collection("SqlServer")]
public class IntegrationTests(SqlServerFixture fixture)
{
    private TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer(fixture.ConnectionString)
            .UseLocking()
            .Options);

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

        sql.Should().Contain("WITH (UPDLOCK, HOLDLOCK, ROWLOCK)");
        sql.Should().NotContain("FOR UPDATE");
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_NoWait_TableHintPresentInSql()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        var sql = ctx.Products.Where(x => x.Id == 1).ForUpdate(LockBehavior.NoWait).ToQueryString();
        sql.Should().Contain("WITH (UPDLOCK, HOLDLOCK, ROWLOCK)");
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForShare_WithTransaction_ThrowsLockingConfigurationException()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        Func<Task> act = async () => await ctx.Products.Where(x => x.Id == 1).ForShare().FirstOrDefaultAsync();
        await act.Should().ThrowAsync<LockingConfigurationException>();

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_SkipLocked_ThrowsLockingConfigurationException()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        Func<Task> act = async () => await ctx.Products.Where(x => x.Id == 1)
            .ForUpdate(LockBehavior.SkipLocked)
            .FirstOrDefaultAsync();

        await act.Should().ThrowAsync<LockingConfigurationException>();
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
    public async Task ForUpdate_WithTimeout_SucceedsOnUncontendedRow()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        var id = await SeedProductAsync(ctx, "Uncontended");

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var row = await ctx.Products.Where(p => p.Id == id)
            .ForUpdate(LockBehavior.Wait, TimeSpan.FromMilliseconds(500))
            .FirstOrDefaultAsync();

        row.Should().NotBeNull();
        await tx.RollbackAsync();
    }
}
