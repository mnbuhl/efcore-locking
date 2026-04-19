using AwesomeAssertions;
using EntityFrameworkCore.Locking;
using EntityFrameworkCore.Locking.Exceptions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.Tests.Infrastructure;

public abstract partial class IntegrationTestsBase
{
    // --- Include / navigation ---

    [Fact]
    public async Task ForUpdate_WithInclude_LoadsNavigationAndLocks()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx, categoryName: "Nav");

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var product = await ctx
            .Products.Include(p => p.Category)
            .Where(p => p.Id == id)
            .ForUpdate()
            .FirstOrDefaultAsync();

        product.Should().NotBeNull();
        product!.Category.Name.Should().Be("Nav");
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_WithIncludeCollection_LoadsOrderLines()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx);
        for (var i = 1; i <= 3; i++)
            ctx.OrderLines.Add(
                new OrderLine
                {
                    ProductId = id,
                    Quantity = i,
                    UnitPrice = i * 1.5m,
                }
            );
        await ctx.SaveChangesAsync();

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var product = await ctx
            .Products.Include(p => p.OrderLines)
            .Where(p => p.Id == id)
            .ForUpdate()
            .FirstOrDefaultAsync();

        product.Should().NotBeNull();
        product!.OrderLines.Should().HaveCount(3);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_WithMultipleIncludes_LocksRootTable()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx, categoryName: "Multi");
        ctx.OrderLines.Add(
            new OrderLine
            {
                ProductId = id,
                Quantity = 1,
                UnitPrice = 5m,
            }
        );
        await ctx.SaveChangesAsync();

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var product = await ctx
            .Products.Include(p => p.Category)
            .Include(p => p.OrderLines)
            .Where(p => p.Id == id)
            .ForUpdate()
            .FirstOrDefaultAsync();

        product.Should().NotBeNull();
        product!.Category.Name.Should().Be("Multi");
        product.OrderLines.Should().HaveCount(1);
        await tx.RollbackAsync();
    }

    // --- Join / relation filter ---

    [Fact]
    public async Task ForUpdate_FilteredByRelation_LocksMatchingRows()
    {
        await using var ctx = CreateContext();
        await SeedAsync(ctx, categoryName: "Gadgets", productName: "Gizmo");
        await SeedAsync(ctx, categoryName: "Other", productName: "Widget");

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var products = await ctx.Products.Where(p => p.Category.Name == "Gadgets").ForUpdate().ToListAsync();

        products.Should().HaveCount(1);
        products[0].Name.Should().Be("Gizmo");
        await tx.RollbackAsync();
    }

    // --- Pagination ---

    [Fact]
    public async Task ForUpdate_WithOrderByAndTake_LocksPage()
    {
        await using var ctx = CreateContext();
        var cat = new Category { Name = "Page" };
        ctx.Categories.Add(cat);
        await ctx.SaveChangesAsync();
        for (var i = 1; i <= 5; i++)
            ctx.Products.Add(
                new Product
                {
                    Name = $"P{i}",
                    Price = i,
                    Stock = i,
                    CategoryId = cat.Id,
                }
            );
        await ctx.SaveChangesAsync();

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var page = await ctx
            .Products.Where(p => p.Category.Name == "Page")
            .OrderBy(p => p.Price)
            .Take(2)
            .ForUpdate()
            .ToListAsync();

        page.Should().HaveCount(2);
        page[0].Price.Should().BeLessThanOrEqualTo(page[1].Price);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_WithSkipAndTake_LocksCorrectPage()
    {
        await using var ctx = CreateContext();
        var cat = new Category { Name = "Skip" };
        ctx.Categories.Add(cat);
        await ctx.SaveChangesAsync();
        for (var i = 1; i <= 5; i++)
            ctx.Products.Add(
                new Product
                {
                    Name = $"Q{i}",
                    Price = i,
                    Stock = i,
                    CategoryId = cat.Id,
                }
            );
        await ctx.SaveChangesAsync();

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var page = await ctx
            .Products.Where(p => p.Category.Name == "Skip")
            .OrderBy(p => p.Price)
            .Skip(2)
            .Take(2)
            .ForUpdate()
            .ToListAsync();

        page.Should().HaveCount(2);
        page[0].Name.Should().Be("Q3");
        page[1].Name.Should().Be("Q4");
        await tx.RollbackAsync();
    }

    // --- AsNoTracking ---

    [Fact]
    public async Task ForUpdate_WithAsNoTracking_ExecutesSuccessfully()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx);

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var product = await ctx.Products.AsNoTracking().Where(p => p.Id == id).ForUpdate().FirstOrDefaultAsync();

        product.Should().NotBeNull();
        ctx.Entry(product!).State.Should().Be(EntityState.Detached);
        await tx.RollbackAsync();
    }

    // --- Unsupported shapes ---

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
