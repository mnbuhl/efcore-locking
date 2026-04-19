using AwesomeAssertions;
using EntityFrameworkCore.Locking;
using EntityFrameworkCore.Locking.PostgreSQL.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.PostgreSQL.Tests;

[Collection("Postgres")]
public class ConcurrencyTests(PostgresFixture fixture) : ConcurrencyTestsBase
{
    protected override TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseNpgsql(fixture.ConnectionString).UseLocking().Options);

    protected override Task ResetDatabaseAsync(TestDbContext ctx) =>
        ctx.Database.ExecuteSqlRawAsync("""TRUNCATE "OrderLines", "Products", "Categories" RESTART IDENTITY CASCADE""");

    // --- PG-specific ---

    [Fact]
    public async Task ForUpdate_MultipleRows_AllLocked_SkipLocked_ReturnsEmpty()
    {
        await using var ctxSeed = CreateContext();
        var id1 = await SeedAsync(ctxSeed, "Bulk1");
        var id2 = await SeedAsync(ctxSeed, "Bulk2");

        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        (await ctxA.Products.Where(p => p.Id == id1 || p.Id == id2).ForUpdate().ToListAsync()).Should().HaveCount(2);

        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();
        (await ctxB.Products.Where(p => p.Id == id1 || p.Id == id2).ForUpdate(LockBehavior.SkipLocked).ToListAsync())
            .Should()
            .BeEmpty();

        await txA.RollbackAsync();
        await txB.RollbackAsync();
    }

    [Fact]
    public async Task ForShare_SkipLocked_SecondTransaction_SkipsForUpdateLockedRow()
    {
        await using var ctxSeed = CreateContext();
        var id = await SeedAsync(ctxSeed, "Share Widget");

        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        (await ctxA.Products.Where(p => p.Id == id).ForShare().FirstOrDefaultAsync()).Should().NotBeNull();

        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();
        (await ctxB.Products.Where(p => p.Id == id).ForUpdate(LockBehavior.SkipLocked).FirstOrDefaultAsync())
            .Should()
            .BeNull();

        await txA.RollbackAsync();
        await txB.RollbackAsync();
    }
}
