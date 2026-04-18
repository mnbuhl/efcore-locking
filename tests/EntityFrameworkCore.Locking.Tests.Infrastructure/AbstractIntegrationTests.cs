using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.Tests.Infrastructure;

public abstract class AbstractIntegrationTests
{
    protected abstract TestDbContext CreateContext();

    protected virtual bool SupportsForShare => true;
    protected virtual bool SupportsSkipLocked => true;

    private async Task<(int categoryId, int productId)> SeedAsync(
        TestDbContext ctx, string categoryName = "Electronics", string productName = "Widget", decimal price = 9.99m, int stock = 100)
    {
        await ctx.Database.EnsureCreatedAsync();
        var category = new Category { Name = categoryName };
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();

        var product = new Product { Name = productName, Price = price, Stock = stock, CategoryId = category.Id };
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();

        return (category.Id, product.Id);
    }

    private async Task<int> SeedWithOrderLinesAsync(TestDbContext ctx, int productId, int lineCount = 3)
    {
        for (var i = 1; i <= lineCount; i++)
            ctx.OrderLines.Add(new OrderLine { ProductId = productId, Quantity = i, UnitPrice = 9.99m * i });
        await ctx.SaveChangesAsync();
        return productId;
    }

    // --- Basic success/failure ---

    [Fact]
    public async Task ForUpdate_WithTransaction_ExecutesSuccessfully()
    {
        await using var ctx = CreateContext();
        var (_, productId) = await SeedAsync(ctx);

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var product = await ctx.Products.Where(p => p.Id == productId).ForUpdate().FirstOrDefaultAsync();

        product.Should().NotBeNull();
        product!.Id.Should().Be(productId);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_WithoutTransaction_ThrowsInvalidOperationException()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();

        Func<Task> act = async () => await ctx.Products.Where(p => p.Id == 1).ForUpdate().FirstOrDefaultAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ForUpdate_NonExistentRow_ReturnsNull()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var result = await ctx.Products.Where(p => p.Id == int.MaxValue).ForUpdate().FirstOrDefaultAsync();

        result.Should().BeNull();
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task SaveChanges_WithoutLock_WorksNormally()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        var category = new Category { Name = "Test" };
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();

        var product = new Product { Name = "NoLock", Price = 1.00m, Stock = 1, CategoryId = category.Id };
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();

        product.Id.Should().BeGreaterThan(0);
    }

    // --- Include / navigation ---

    [Fact]
    public async Task ForUpdate_WithInclude_LoadsNavigationAndLocks()
    {
        await using var ctx = CreateContext();
        var (_, productId) = await SeedAsync(ctx);

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var product = await ctx.Products
            .Include(p => p.Category)
            .Where(p => p.Id == productId)
            .ForUpdate()
            .FirstOrDefaultAsync();

        product.Should().NotBeNull();
        product!.Category.Should().NotBeNull();
        product.Category.Name.Should().Be("Electronics");
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_WithIncludeCollection_LoadsOrderLines()
    {
        await using var ctx = CreateContext();
        var (_, productId) = await SeedAsync(ctx);
        await SeedWithOrderLinesAsync(ctx, productId, lineCount: 3);

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var product = await ctx.Products
            .Include(p => p.OrderLines)
            .Where(p => p.Id == productId)
            .ForUpdate()
            .FirstOrDefaultAsync();

        product.Should().NotBeNull();
        product!.OrderLines.Should().HaveCount(3);
        await tx.RollbackAsync();
    }

    // --- Join / relation filter ---

    [Fact]
    public async Task ForUpdate_FilteredByRelation_LocksMatchingRows()
    {
        await using var ctx = CreateContext();
        var (_, productId) = await SeedAsync(ctx, categoryName: "Gadgets", productName: "Gizmo");
        await SeedAsync(ctx, categoryName: "Other", productName: "Widget");

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var products = await ctx.Products
            .Where(p => p.Category.Name == "Gadgets")
            .ForUpdate()
            .ToListAsync();

        products.Should().HaveCount(1);
        products[0].Name.Should().Be("Gizmo");
        await tx.RollbackAsync();
    }

    // --- Pagination ---

    [Fact]
    public async Task ForUpdate_WithOrderByAndTake_LocksPage()
    {
        await using var ctx = CreateContext();
        var (_, _) = await SeedAsync(ctx, productName: "A", price: 1m);
        await using var ctx2 = CreateContext();
        var (_, _) = await SeedAsync(ctx2, productName: "B", price: 2m);
        await using var ctx3 = CreateContext();
        var (_, _) = await SeedAsync(ctx3, productName: "C", price: 3m);

        await using var ctx4 = CreateContext();
        await using var tx = await ctx4.Database.BeginTransactionAsync();
        var page = await ctx4.Products
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
        await ctx.Database.EnsureCreatedAsync();
        var category = new Category { Name = "Page" };
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();

        for (var i = 1; i <= 5; i++)
            ctx.Products.Add(new Product { Name = $"P{i}", Price = i, Stock = i, CategoryId = category.Id });
        await ctx.SaveChangesAsync();

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var page = await ctx.Products
            .Where(p => p.Category.Name == "Page")
            .OrderBy(p => p.Price)
            .Skip(2).Take(2)
            .ForUpdate()
            .ToListAsync();

        page.Should().HaveCount(2);
        page[0].Name.Should().Be("P3");
        page[1].Name.Should().Be("P4");
        await tx.RollbackAsync();
    }

    // --- Multiple rows / ToList ---

    [Fact]
    public async Task ForUpdate_ToList_LocksMultipleRows()
    {
        await using var ctx = CreateContext();
        var (_, id1) = await SeedAsync(ctx, productName: "Bulk1");
        await using var ctx2 = CreateContext();
        var (_, id2) = await SeedAsync(ctx2, productName: "Bulk2");

        await using var ctx3 = CreateContext();
        await using var tx = await ctx3.Database.BeginTransactionAsync();
        var products = await ctx3.Products
            .Where(p => p.Id == id1 || p.Id == id2)
            .ForUpdate()
            .ToListAsync();

        products.Should().HaveCount(2);
        await tx.RollbackAsync();
    }

    // --- ForShare ---

    [Fact]
    public virtual async Task ForShare_WithTransaction_ReturnsData()
    {
        if (!SupportsForShare)
            return;

        await using var ctx = CreateContext();
        var (_, productId) = await SeedAsync(ctx, productName: "Share Me");

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var product = await ctx.Products.Where(p => p.Id == productId).ForShare().FirstOrDefaultAsync();

        product.Should().NotBeNull();
        product!.Name.Should().Be("Share Me");
        await tx.RollbackAsync();
    }

    [Fact]
    public virtual async Task ForShare_TwoConcurrentReaders_BothSucceed()
    {
        if (!SupportsForShare)
            return;

        await using var ctx = CreateContext();
        var (_, productId) = await SeedAsync(ctx, productName: "Shared");

        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        var rowA = await ctxA.Products.Where(p => p.Id == productId).ForShare().FirstOrDefaultAsync();
        rowA.Should().NotBeNull();

        // Shared lock is compatible with another shared lock — second reader must not block
        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();
        var rowB = await ctxB.Products.Where(p => p.Id == productId).ForShare().FirstOrDefaultAsync();
        rowB.Should().NotBeNull();

        await txA.RollbackAsync();
        await txB.RollbackAsync();
    }

    // --- SkipLocked ---

    [Fact]
    public virtual async Task ForUpdate_SkipLocked_SecondTransaction_SkipsLockedRow()
    {
        if (!SupportsSkipLocked)
            return;

        await using var ctx = CreateContext();
        var (_, productId) = await SeedAsync(ctx, productName: "Skip Me");

        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        (await ctxA.Products.Where(p => p.Id == productId).ForUpdate().FirstOrDefaultAsync()).Should().NotBeNull();

        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();
        var skipped = await ctxB.Products.Where(p => p.Id == productId)
            .ForUpdate(LockBehavior.SkipLocked)
            .FirstOrDefaultAsync();

        skipped.Should().BeNull();

        await txA.RollbackAsync();
        await txB.RollbackAsync();
    }

    [Fact]
    public virtual async Task ForUpdate_SkipLocked_MultipleRows_ReturnsOnlyUnlocked()
    {
        if (!SupportsSkipLocked)
            return;

        await using var ctx = CreateContext();
        var (_, id1) = await SeedAsync(ctx, productName: "Locked");
        await using var ctx2 = CreateContext();
        var (_, id2) = await SeedAsync(ctx2, productName: "Free");

        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        await ctxA.Products.Where(p => p.Id == id1).ForUpdate().FirstOrDefaultAsync();

        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();
        var results = await ctxB.Products
            .Where(p => p.Id == id1 || p.Id == id2)
            .ForUpdate(LockBehavior.SkipLocked)
            .ToListAsync();

        results.Should().HaveCount(1);
        results[0].Id.Should().Be(id2);

        await txA.RollbackAsync();
        await txB.RollbackAsync();
    }

    // --- Concurrency / NoWait ---

    [Fact]
    public async Task ForUpdate_NoWait_ThrowsLockTimeoutExceptionOnLockedRow()
    {
        await using var ctx = CreateContext();
        var (_, productId) = await SeedAsync(ctx, productName: "NoWait");

        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        await ctxA.Products.Where(p => p.Id == productId).ForUpdate().FirstOrDefaultAsync();

        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();

        Func<Task> act = async () => await ctxB.Products.Where(p => p.Id == productId)
            .ForUpdate(LockBehavior.NoWait)
            .FirstOrDefaultAsync();

        await act.Should().ThrowAsync<LockTimeoutException>();

        await txA.RollbackAsync();
        await txB.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_CommitAndReread_SeesUpdatedData()
    {
        await using var ctx = CreateContext();
        var (_, productId) = await SeedAsync(ctx, productName: "Versioned");

        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        var rowA = await ctxA.Products.Where(p => p.Id == productId).ForUpdate().FirstAsync();
        rowA.Price = 99.99m;
        await ctxA.SaveChangesAsync();
        await txA.CommitAsync();

        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();
        var rowB = await ctxB.Products.Where(p => p.Id == productId).ForUpdate().FirstAsync();
        rowB.Price.Should().Be(99.99m);
        await txB.RollbackAsync();
    }

    // --- Unsupported shapes ---

    [Fact]
    public async Task ForUpdate_UnionQuery_ThrowsLockingConfigurationException()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        var query = ctx.Products.Where(p => p.Id == 1)
            .Union(ctx.Products.Where(p => p.Id == 2))
            .ForUpdate();

        Func<Task> act = async () => await query.ToListAsync();
        await act.Should().ThrowAsync<LockingConfigurationException>();

        await tx.RollbackAsync();
    }
}
