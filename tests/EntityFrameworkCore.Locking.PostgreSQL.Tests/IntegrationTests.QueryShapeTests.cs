using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.PostgreSQL.Tests;

public partial class IntegrationTests
{
    [Fact]
    public async Task ForUpdate_WithThenInclude_LoadsNestedNavigation()
    {
        await using var ctx = CreateContext();
        var (catId, _) = await SeedAsync(ctx, categoryName: "ThenInclude");
        ctx.Products.Add(
            new Product
            {
                Name = "Second",
                Price = 1m,
                Stock = 1,
                CategoryId = catId,
            }
        );
        await ctx.SaveChangesAsync();

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var category = await ctx
            .Categories.Include(c => c.Products)
                .ThenInclude(p => p.OrderLines)
            .Where(c => c.Name == "ThenInclude")
            .ForUpdate()
            .FirstOrDefaultAsync();

        category.Should().NotBeNull();
        category.Products.Should().HaveCount(2);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_ExceptQuery_ThrowsLockingConfigurationException()
    {
        await using var ctx = CreateContext();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        Func<Task> act = async () =>
            await ctx
                .Products.Where(p => p.Id == 1)
                .Except(ctx.Products.Where(p => p.Id == 2))
                .ForUpdate()
                .ToListAsync();

        await act.Should().ThrowAsync<LockingConfigurationException>();
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_IntersectQuery_ThrowsLockingConfigurationException()
    {
        await using var ctx = CreateContext();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        Func<Task> act = async () =>
            await ctx
                .Products.Where(p => p.Id == 1)
                .Intersect(ctx.Products.Where(p => p.Id == 2))
                .ForUpdate()
                .ToListAsync();

        await act.Should().ThrowAsync<LockingConfigurationException>();
        await tx.RollbackAsync();
    }
}
