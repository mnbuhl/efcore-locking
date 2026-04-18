using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.PostgreSQL.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.PostgreSQL.Tests;

[Collection("Postgres")]
public class IntegrationTests(PostgresFixture fixture) : AbstractIntegrationTests
{
    protected override TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseLocking()
            .Options);

    // --- PostgreSQL-specific SQL assertions ---

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

        ctx.Products.Where(p => p.Id == 1).ForUpdate(LockBehavior.NoWait).ToQueryString()
            .Should().Contain("FOR UPDATE NOWAIT");
    }

    [Fact]
    public async Task ForUpdate_SkipLocked_GeneratesExactSql()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();

        ctx.Products.Where(p => p.Id == 1).ForUpdate(LockBehavior.SkipLocked).ToQueryString()
            .Should().Contain("FOR UPDATE SKIP LOCKED");
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

        ctx.Products.Where(p => p.Id == 1).ForShare(LockBehavior.NoWait).ToQueryString()
            .Should().Contain("FOR SHARE NOWAIT");
    }

    [Fact]
    public async Task ForShare_SkipLocked_GeneratesExactSql()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();

        ctx.Products.Where(p => p.Id == 1).ForShare(LockBehavior.SkipLocked).ToQueryString()
            .Should().Contain("FOR SHARE SKIP LOCKED");
    }

    [Fact]
    public async Task ForUpdate_WithTimeout_SucceedsOnUncontendedRow()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        var category = new Category { Name = "Timeout" };
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();
        var product = new Product { Name = "Uncontended", Price = 1m, Stock = 1, CategoryId = category.Id };
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();

        await using var ctx2 = CreateContext();
        await using var tx2 = await ctx2.Database.BeginTransactionAsync();
        var row = await ctx2.Products.Where(p => p.Id == product.Id)
            .ForUpdate(LockBehavior.Wait, TimeSpan.FromMilliseconds(500))
            .FirstOrDefaultAsync();

        row.Should().NotBeNull();
        await tx2.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_UnsupportedShape_Except_ThrowsLockingConfigurationException()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        Func<Task> act = async () => await ctx.Products.Where(p => p.Id == 1)
            .Except(ctx.Products.Where(p => p.Id == 2))
            .ForUpdate()
            .ToListAsync();

        await act.Should().ThrowAsync<LockingConfigurationException>();
        await tx.RollbackAsync();
    }
}
