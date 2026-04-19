using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.PostgreSQL;
using EntityFrameworkCore.Locking.PostgreSQL.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.PostgreSQL.Tests;

[Collection("Postgres")]
public partial class IntegrationTests(PostgresFixture fixture) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        await ctx.Database.ExecuteSqlRawAsync(
            """TRUNCATE "OrderLines", "Products", "Categories" RESTART IDENTITY CASCADE"""
        );
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private TestDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<TestDbContext>()
                .UseNpgsql(fixture.ConnectionString)
                .UseLocking()
                .Options
        );

    private (TestDbContext ctx, SqlCapture capture) CreateContextWithCapture()
    {
        var capture = new SqlCapture();
        var ctx = new TestDbContext(
            new DbContextOptionsBuilder<TestDbContext>()
                .UseNpgsql(fixture.ConnectionString)
                .UseLocking()
                .AddInterceptors(capture)
                .Options
        );
        return (ctx, capture);
    }

    private async Task<(int categoryId, int productId)> SeedAsync(
        TestDbContext ctx,
        string categoryName = "Electronics",
        string productName = "Widget",
        decimal price = 9.99m
    )
    {
        var cat = new Category { Name = categoryName };
        ctx.Categories.Add(cat);
        await ctx.SaveChangesAsync();
        var p = new Product
        {
            Name = productName,
            Price = price,
            Stock = 10,
            CategoryId = cat.Id,
        };
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

        Func<Task> act = async () =>
            await ctx.Products.Where(p => p.Id == 1).ForUpdate().FirstOrDefaultAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ForUpdate_NonExistentRow_ReturnsNull()
    {
        await using var ctx = CreateContext();

        await using var tx = await ctx.Database.BeginTransactionAsync();
        (await ctx.Products.Where(p => p.Id == int.MaxValue).ForUpdate().FirstOrDefaultAsync())
            .Should()
            .BeNull();
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task SaveChanges_WithoutLock_WorksNormally()
    {
        await using var ctx = CreateContext();
        var cat = new Category { Name = "NoLock" };
        ctx.Categories.Add(cat);
        await ctx.SaveChangesAsync();
        var p = new Product
        {
            Name = "NoLock",
            Price = 1m,
            Stock = 1,
            CategoryId = cat.Id,
        };
        ctx.Products.Add(p);
        await ctx.SaveChangesAsync();

        p.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ForUpdate_LockContext_DoesNotLeakToSaveChanges()
    {
        // Verifies that LockContext is cleared after a locking query so that
        // SaveChanges' internal INSERT...RETURNING is not accidentally wrapped.
        await using var ctx = CreateContext();
        var (catId, _) = await SeedAsync(ctx);

        await using var tx = await ctx.Database.BeginTransactionAsync();
        await ctx.Products.Where(p => p.Id == 1).ForUpdate().FirstOrDefaultAsync();

        // SaveChanges would throw InvalidCastException if LockContext leaked
        var p = new Product
        {
            Name = "AfterLock",
            Price = 1m,
            Stock = 1,
            CategoryId = catId,
        };
        ctx.Products.Add(p);
        await ctx.SaveChangesAsync();
        p.Id.Should().BeGreaterThan(0);

        await tx.CommitAsync();
    }

    [Fact]
    public async Task ForUpdate_ToList_ReturnsAllMatchingRows()
    {
        await using var ctx = CreateContext();
        var (catId, _) = await SeedAsync(ctx);
        ctx.Products.Add(
            new Product
            {
                Name = "B",
                Price = 2m,
                Stock = 1,
                CategoryId = catId,
            }
        );
        ctx.Products.Add(
            new Product
            {
                Name = "C",
                Price = 3m,
                Stock = 1,
                CategoryId = catId,
            }
        );
        await ctx.SaveChangesAsync();

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var products = await ctx.Products.ForUpdate().ToListAsync();

        products.Should().HaveCount(3);
        await tx.RollbackAsync();
    }
}
