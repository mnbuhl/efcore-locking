using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.PostgreSQL.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
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

    private async Task<(int categoryId, int productId)> SeedAsync(
        TestDbContext ctx, string categoryName = "Electronics", string productName = "Widget")
    {
        await ctx.Database.EnsureCreatedAsync();
        var cat = new Category { Name = categoryName };
        ctx.Categories.Add(cat);
        await ctx.SaveChangesAsync();
        var p = new Product { Name = productName, Price = 9.99m, Stock = 10, CategoryId = cat.Id };
        ctx.Products.Add(p);
        await ctx.SaveChangesAsync();
        return (cat.Id, p.Id);
    }

    // --- Basic success/failure ---

    [Fact]
    public async Task ForUpdate_WithTransaction_ExecutesSuccessfully()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx);

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var product = await ctx.Products.Where(p => p.Id == id).ForUpdate().FirstOrDefaultAsync();

        product.Should().NotBeNull();
        product!.Id.Should().Be(id);
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
        (await ctx.Products.Where(p => p.Id == int.MaxValue).ForUpdate().FirstOrDefaultAsync()).Should().BeNull();
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task SaveChanges_WithoutLock_WorksNormally()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        var cat = new Category { Name = "NoLock" };
        ctx.Categories.Add(cat);
        await ctx.SaveChangesAsync();
        var p = new Product { Name = "NoLock", Price = 1m, Stock = 1, CategoryId = cat.Id };
        ctx.Products.Add(p);
        await ctx.SaveChangesAsync();

        p.Id.Should().BeGreaterThan(0);
    }

    // --- SQL assertions ---

    [Fact]
    public void ForUpdate_GeneratesExactSql()
    {
        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();

        var sql = ctx.Products.Where(p => p.Id == 1).ForUpdate().ToQueryString();
        sql.Should().Contain("FOR UPDATE");
        sql.Should().NotContain("FOR SHARE");
        sql.Should().NotContain("NOWAIT");
        sql.Should().NotContain("SKIP LOCKED");
    }

    [Fact]
    public void ForUpdate_NoWait_GeneratesExactSql()
    {
        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();

        ctx.Products.Where(p => p.Id == 1).ForUpdate(LockBehavior.NoWait).ToQueryString()
            .Should().Contain("FOR UPDATE NOWAIT");
    }

    [Fact]
    public void ForUpdate_SkipLocked_GeneratesExactSql()
    {
        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();

        ctx.Products.Where(p => p.Id == 1).ForUpdate(LockBehavior.SkipLocked).ToQueryString()
            .Should().Contain("FOR UPDATE SKIP LOCKED");
    }

    [Fact]
    public void ForShare_GeneratesExactSql()
    {
        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();

        var sql = ctx.Products.Where(p => p.Id == 1).ForShare().ToQueryString();
        sql.Should().Contain("FOR SHARE");
        sql.Should().NotContain("FOR UPDATE");
    }

    [Fact]
    public void ForShare_NoWait_GeneratesExactSql()
    {
        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();

        ctx.Products.Where(p => p.Id == 1).ForShare(LockBehavior.NoWait).ToQueryString()
            .Should().Contain("FOR SHARE NOWAIT");
    }

    [Fact]
    public void ForShare_SkipLocked_GeneratesExactSql()
    {
        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();

        ctx.Products.Where(p => p.Id == 1).ForShare(LockBehavior.SkipLocked).ToQueryString()
            .Should().Contain("FOR SHARE SKIP LOCKED");
    }

    // --- Include / navigation ---

    [Fact]
    public async Task ForUpdate_WithInclude_LoadsNavigationAndLocks()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx, categoryName: "Nav");

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var product = await ctx.Products.Include(p => p.Category)
            .Where(p => p.Id == id).ForUpdate().FirstOrDefaultAsync();

        product.Should().NotBeNull();
        product!.Category.Name.Should().Be("Nav");
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_ThenLoadCollection_LocksRowAndLoadsOrderLines()
    {
        // PostgreSQL prohibits FOR UPDATE on the nullable side of a LEFT JOIN (collections).
        // The correct pattern: lock the row first, then load the collection separately.
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx);
        for (var i = 1; i <= 3; i++)
            ctx.OrderLines.Add(new OrderLine { ProductId = id, Quantity = i, UnitPrice = i * 1.5m });
        await ctx.SaveChangesAsync();

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var product = await ctx.Products.Where(p => p.Id == id).ForUpdate().FirstOrDefaultAsync();
        product.Should().NotBeNull();

        await ctx.Entry(product!).Collection(p => p.OrderLines).LoadAsync();
        product.OrderLines.Should().HaveCount(3);
        await tx.RollbackAsync();
    }

    // --- Join / relation filter ---

    [Fact]
    public async Task ForUpdate_FilteredByRelation_LocksMatchingRows()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        await using var ctx = CreateContext();
        await SeedAsync(ctx, categoryName: $"Gadgets-{tag}", productName: "Gizmo");
        await SeedAsync(ctx, categoryName: $"Other-{tag}", productName: "Widget");

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var products = await ctx.Products
            .Where(p => p.Category.Name == $"Gadgets-{tag}")
            .ForUpdate().ToListAsync();

        products.Should().HaveCount(1);
        products[0].Name.Should().Be("Gizmo");
        await tx.RollbackAsync();
    }

    // --- Pagination ---

    [Fact]
    public async Task ForUpdate_WithOrderByAndTake_LocksPage()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        var cat = new Category { Name = "Page" };
        ctx.Categories.Add(cat);
        await ctx.SaveChangesAsync();
        for (var i = 1; i <= 5; i++)
            ctx.Products.Add(new Product { Name = $"P{i}", Price = i, Stock = i, CategoryId = cat.Id });
        await ctx.SaveChangesAsync();

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var page = await ctx.Products.Where(p => p.Category.Name == "Page")
            .OrderBy(p => p.Price).Take(2).ForUpdate().ToListAsync();

        page.Should().HaveCount(2);
        page[0].Price.Should().BeLessThanOrEqualTo(page[1].Price);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_WithSkipAndTake_LocksCorrectPage()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        var cat = new Category { Name = "Skip" };
        ctx.Categories.Add(cat);
        await ctx.SaveChangesAsync();
        for (var i = 1; i <= 5; i++)
            ctx.Products.Add(new Product { Name = $"Q{i}", Price = i, Stock = i, CategoryId = cat.Id });
        await ctx.SaveChangesAsync();

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var page = await ctx.Products.Where(p => p.Category.Name == "Skip")
            .OrderBy(p => p.Price).Skip(2).Take(2).ForUpdate().ToListAsync();

        page.Should().HaveCount(2);
        page[0].Name.Should().Be("Q3");
        page[1].Name.Should().Be("Q4");
        await tx.RollbackAsync();
    }

    // --- ForShare ---

    [Fact]
    public async Task ForShare_WithTransaction_ReturnsData()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx, productName: "Share Me");

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var product = await ctx.Products.Where(p => p.Id == id).ForShare().FirstOrDefaultAsync();

        product.Should().NotBeNull();
        product!.Name.Should().Be("Share Me");
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForShare_TwoConcurrentReaders_BothSucceed()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx, productName: "Shared");

        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        (await ctxA.Products.Where(p => p.Id == id).ForShare().FirstOrDefaultAsync()).Should().NotBeNull();

        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();
        (await ctxB.Products.Where(p => p.Id == id).ForShare().FirstOrDefaultAsync()).Should().NotBeNull();

        await txA.RollbackAsync();
        await txB.RollbackAsync();
    }

    // --- Unsupported shapes ---

    [Fact]
    public async Task ForUpdate_UnsupportedShape_Except_ThrowsLockingConfigurationException()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        Func<Task> act = async () => await ctx.Products.Where(p => p.Id == 1)
            .Except(ctx.Products.Where(p => p.Id == 2))
            .ForUpdate().ToListAsync();

        await act.Should().ThrowAsync<LockingConfigurationException>();
        await tx.RollbackAsync();
    }

    // --- Timeout ---

    [Fact]
    public async Task ForUpdate_WithTimeout_SucceedsOnUncontendedRow()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx, productName: "Uncontended");

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var row = await ctx.Products.Where(p => p.Id == id)
            .ForUpdate(LockBehavior.Wait, TimeSpan.FromMilliseconds(500))
            .FirstOrDefaultAsync();

        row.Should().NotBeNull();
        await tx.RollbackAsync();
    }
}
