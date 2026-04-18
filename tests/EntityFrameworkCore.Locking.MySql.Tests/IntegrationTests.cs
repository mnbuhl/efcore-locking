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

    [Fact]
    public async Task ForUpdate_WithTransaction_ExecutesSuccessfully()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();

        var p = new Product { Name = "Test", Price = 1.99m };
        ctx.Products.Add(p);
        await ctx.SaveChangesAsync();

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var row = await ctx.Products.Where(x => x.Id == p.Id).ForUpdate().FirstOrDefaultAsync();
        Assert.NotNull(row);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_WithoutTransaction_ThrowsInvalidOperationException()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ctx.Products.Where(x => x.Id == 1).ForUpdate().FirstOrDefaultAsync());
    }

    [Fact]
    public async Task ForUpdate_GeneratesSqlContainingForUpdate()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        var sql = ctx.Products.Where(x => x.Id == 1).ForUpdate().ToQueryString();
        Assert.Contains("FOR UPDATE", sql, StringComparison.OrdinalIgnoreCase);
        await tx.RollbackAsync();
    }
}
