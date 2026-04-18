using EntityFrameworkCore.Locking.PostgreSQL.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.PostgreSQL.Tests;

[Collection("Postgres")]
public class IntegrationTests(PostgresFixture fixture)
{
    private TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseLocking()
            .Options;
        return new TestDbContext(options);
    }

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

        Assert.NotNull(product);
        Assert.Equal(id, product.Id);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_WithoutTransaction_ThrowsInvalidOperationException()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        var id = await SeedProductAsync(ctx);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ctx.Products
                .Where(p => p.Id == id)
                .ForUpdate()
                .FirstOrDefaultAsync());
    }

    [Fact]
    public async Task ForUpdate_GeneratesSqlContainingForUpdate()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();

        var query = ctx.Products.Where(p => p.Id == 1).ForUpdate();
        var sql = query.ToQueryString();

        Assert.Contains("FOR UPDATE", sql, StringComparison.OrdinalIgnoreCase);
    }
}
