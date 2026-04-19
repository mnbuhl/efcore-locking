using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.SqlServer.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.SqlServer.Tests;

[Collection("SqlServer")]
public class ConcurrencyTests(SqlServerFixture fixture) : ConcurrencyTestsBase
{
    // SqlServer honors sub-second LOCK_TIMEOUT; preserve original 500ms behavior.
    protected override TimeSpan WaitTimeout => TimeSpan.FromMilliseconds(500);

    protected override TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseSqlServer(fixture.ConnectionString).UseLocking().Options);

    protected override async Task ResetDatabaseAsync(TestDbContext ctx)
    {
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM [OrderLines]");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM [Products]");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM [Categories]");
        await ctx.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[OrderLines]', RESEED, 0)");
        await ctx.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[Products]', RESEED, 0)");
        await ctx.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[Categories]', RESEED, 0)");
    }

    // --- SqlServer-specific ---

    [Fact]
    public async Task ForShare_Throws_LockingConfigurationException()
    {
        await using var ctx = CreateContext();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        Func<Task> act = async () => await ctx.Products.Where(p => p.Id == 1).ForShare().FirstOrDefaultAsync();
        await act.Should().ThrowAsync<LockingConfigurationException>();
        await tx.RollbackAsync();
    }
}
