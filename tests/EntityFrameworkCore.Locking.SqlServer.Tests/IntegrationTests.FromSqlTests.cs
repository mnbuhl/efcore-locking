using AwesomeAssertions;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.SqlServer.Tests;

public partial class IntegrationTests
{
    [Fact]
    public async Task ForUpdate_WhenFromSqlRaw_WhenInsideTransaction_ShouldExecuteSuccessfully()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx);

        await using var tx = await ctx.Database.BeginTransactionAsync();

        var products = await ctx
            .Products.FromSqlRaw("SELECT * FROM [Products]")
            .Where(p => p.Id == id)
            .ForUpdate()
            .ToListAsync();

        products.Should().HaveCount(1);
        products[0].Id.Should().Be(id);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_WhenFromSqlInterpolated_WhenInsideTransaction_ShouldExecuteSuccessfully()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx);

        await using var tx = await ctx.Database.BeginTransactionAsync();

        var products = await ctx
            .Products.FromSql($"SELECT * FROM [Products] WHERE [Id] = {id}")
            .ForUpdate()
            .ToListAsync();

        products.Should().HaveCount(1);
        products[0].Id.Should().Be(id);

        await tx.RollbackAsync();
    }
}
