using EntityFrameworkCore.Locking.SqlServer.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.SqlServer.Tests;

[Collection("SqlServer")]
public partial class IntegrationTests(SqlServerFixture fixture) : IntegrationTestsBase
{
    protected override TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseSqlServer(fixture.ConnectionString).UseLocking().Options);

    protected override (TestDbContext ctx, SqlCapture capture) CreateContextWithCapture()
    {
        var capture = new SqlCapture();
        var ctx = new TestDbContext(
            new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlServer(fixture.ConnectionString)
                .UseLocking()
                .AddInterceptors(capture)
                .Options
        );
        return (ctx, capture);
    }

    protected override async Task ResetDatabaseAsync(TestDbContext ctx)
    {
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM [OrderLines]");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM [Products]");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM [Categories]");
        await ctx.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[OrderLines]', RESEED, 0)");
        await ctx.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[Products]', RESEED, 0)");
        await ctx.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[Categories]', RESEED, 0)");
    }
}
