using AwesomeAssertions;
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
        product.Category.Name.Should().Be("Nav");
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
        product.OrderLines.Should().HaveCount(3);
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
        product.Category.Name.Should().Be("Multi");
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
        ctx.Entry(product).State.Should().Be(EntityState.Detached);
        await tx.RollbackAsync();
    }

    // --- Subquery shapes ---

    [Fact]
    public async Task ForUpdate_WithContainsInWhere_LocksMatchingRows()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx);

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var ids = new List<int> { id };
        var products = await ctx.Products.Where(p => ids.Contains(p.Id)).ForUpdate().ToListAsync();

        products.Should().HaveCount(1);
        products[0].Id.Should().Be(id);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_WithCorrelatedSubqueryInWhere_LocksMatchingRows()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx);
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
        // Correlated subquery in WHERE: lock applies to the outer SELECT's rows
        var products = await ctx
            .Products.Where(p => ctx.OrderLines.Any(ol => ol.ProductId == p.Id))
            .ForUpdate()
            .ToListAsync();

        products.Should().HaveCount(1);
        products[0].Id.Should().Be(id);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_WithChainedWhere_LocksMatchingRows()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx, price: 5m);

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var product = await ctx
            .Products.Where(p => p.Price > 1m)
            .Where(p => p.Id == id)
            .ForUpdate()
            .FirstOrDefaultAsync();

        product.Should().NotBeNull();
        product.Id.Should().Be(id);
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

    [Fact]
    public async Task ForUpdate_DistinctQuery_ThrowsLockingConfigurationException()
    {
        await using var ctx = CreateContext();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        Func<Task> act = async () => await ctx.Products.Distinct().ForUpdate().ToListAsync();

        await act.Should().ThrowAsync<LockingConfigurationException>();
        await tx.RollbackAsync();
    }

    // --- Set operations: Concat ---

    [Fact]
    public async Task ForUpdate_WhenConcatQuery_ShouldThrowLockingConfigurationException()
    {
        await using var ctx = CreateContext();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        // Concat → UNION ALL → SetOperationBase → must be rejected
        Func<Task> act = async () =>
            await ctx
                .Products.Where(p => p.Id == 1)
                .Concat(ctx.Products.Where(p => p.Id == 2))
                .ForUpdate()
                .ToListAsync();

        await act.Should().ThrowAsync<LockingConfigurationException>();
        await tx.RollbackAsync();
    }

    // --- Explicit join shapes ---

    [Fact]
    public async Task ForUpdate_WithInnerJoinQuerySyntax_WhenCondition_ShouldReturnLockedRows()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx, categoryName: "JoinTest");

        await using var tx = await ctx.Database.BeginTransactionAsync();

        var products = await (
            from p in ctx.Products
            join c in ctx.Categories on p.CategoryId equals c.Id
            where c.Name == "JoinTest"
            select p
        )
            .ForUpdate()
            .ToListAsync();

        products.Should().HaveCount(1);
        products[0].Id.Should().Be(id);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_WithSelectMany_WhenCondition_ShouldReturnLockedRows()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx);
        ctx.OrderLines.Add(
            new OrderLine
            {
                ProductId = id,
                Quantity = 1,
                UnitPrice = 1m,
            }
        );
        await ctx.SaveChangesAsync();

        await using var tx = await ctx.Database.BeginTransactionAsync();

        var products = await ctx
            .Products.SelectMany(p => ctx.OrderLines.Where(ol => ol.ProductId == p.Id), (p, _) => p)
            .Where(p => p.Id == id)
            .ForUpdate()
            .ToListAsync();

        products.Should().HaveCount(1);
        products[0].Id.Should().Be(id);
        await tx.RollbackAsync();
    }

    // --- Tags interaction ---

    [Fact]
    public async Task ForUpdate_WhenTagWithBeforeLock_ShouldPreserveUserTagInExecutedSql()
    {
        var (ctx, cap) = CreateContextWithCapture();
        await using (ctx)
        {
            var (_, id) = await SeedAsync(ctx);

            await using var tx = await ctx.Database.BeginTransactionAsync();

            await ctx.Products.Where(p => p.Id == id).TagWith("user-tag-before").ForUpdate().FirstOrDefaultAsync();

            cap.LastCommand.Should().NotBeNull();
            cap.LastCommand!.Should().Contain("user-tag-before");
            cap.LastCommand.Should().Contain("__efcore_locking");

            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task ForUpdate_WhenTagWithAfterLock_ShouldPreserveUserTagInExecutedSql()
    {
        var (ctx, cap) = CreateContextWithCapture();
        await using (ctx)
        {
            var (_, id) = await SeedAsync(ctx);

            await using var tx = await ctx.Database.BeginTransactionAsync();

            await ctx.Products.Where(p => p.Id == id).ForUpdate().TagWith("user-tag-after").FirstOrDefaultAsync();

            cap.LastCommand.Should().NotBeNull();
            cap.LastCommand!.Should().Contain("user-tag-after");
            cap.LastCommand.Should().Contain("__efcore_locking");

            await tx.RollbackAsync();
        }
    }
}
