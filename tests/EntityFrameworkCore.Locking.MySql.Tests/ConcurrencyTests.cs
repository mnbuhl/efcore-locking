using AwesomeAssertions;
using EntityFrameworkCore.Locking;
using EntityFrameworkCore.Locking.MySql.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.MySql.Tests;

[Collection("MySql")]
public class ConcurrencyTests(MySqlFixture fixture) : ConcurrencyTestsBase
{
    // MySQL innodb_lock_wait_timeout is integer seconds; 200ms rounds down to 0 which is invalid.
    protected override TimeSpan WaitTimeout => TimeSpan.FromSeconds(1);

    protected override TestDbContext CreateContext()
    {
        var serverVersion = ServerVersion.AutoDetect(fixture.ConnectionString);
        return new(
            new DbContextOptionsBuilder<TestDbContext>()
                .UseMySql(fixture.ConnectionString, serverVersion)
                .UseLocking()
                .Options
        );
    }

    protected override async Task ResetDatabaseAsync(TestDbContext ctx)
    {
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `OrderLines`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `Products`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `Categories`");
    }

    // --- MySQL-specific ---

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
}
