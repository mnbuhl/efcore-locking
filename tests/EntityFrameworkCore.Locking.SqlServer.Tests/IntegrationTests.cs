using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.SqlServer.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
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
        var row = await ctx.Products.Where(p => p.Id == id).ForUpdate().FirstOrDefaultAsync();

        row.Should().NotBeNull();
        row!.Id.Should().Be(id);
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
    public async Task ForUpdate_GeneratesExactSql()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        var sql = ctx.Products.Where(p => p.Id == 1).ForUpdate().ToQueryString();
        sql.Should().Contain("WITH (UPDLOCK, HOLDLOCK, ROWLOCK)");
        sql.Should().NotContain("FOR UPDATE");
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_SkipLocked_GeneratesReadPastHint()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        var sql = ctx.Products.Where(p => p.Id == 1).ForUpdate(LockBehavior.SkipLocked).ToQueryString();
        sql.Should().Contain("WITH (UPDLOCK, ROWLOCK, READPAST)");
        sql.Should().NotContain("HOLDLOCK");
        await tx.RollbackAsync();
    }

    // --- Provider-unsupported features ---

    [Fact]
    public async Task ForShare_ThrowsLockingConfigurationException()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        Func<Task> act = async () => await ctx.Products.Where(p => p.Id == 1).ForShare().FirstOrDefaultAsync();
        await act.Should().ThrowAsync<LockingConfigurationException>();
        await tx.RollbackAsync();
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
    public async Task ForUpdate_WithIncludeCollection_LoadsOrderLines()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx);
        for (var i = 1; i <= 3; i++)
            ctx.OrderLines.Add(new OrderLine { ProductId = id, Quantity = i, UnitPrice = i * 1.5m });
        await ctx.SaveChangesAsync();

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var product = await ctx.Products.Include(p => p.OrderLines)
            .Where(p => p.Id == id).ForUpdate().FirstOrDefaultAsync();

        product.Should().NotBeNull();
        product!.OrderLines.Should().HaveCount(3);
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
