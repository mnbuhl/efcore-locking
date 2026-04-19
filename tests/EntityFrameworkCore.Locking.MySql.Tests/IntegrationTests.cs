using EntityFrameworkCore.Locking.MySql.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.MySql.Tests;

[Collection("MySql")]
public partial class IntegrationTests(MySqlFixture fixture) : IntegrationTestsBase
{
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

    protected override (TestDbContext ctx, SqlCapture capture) CreateContextWithCapture()
    {
        var capture = new SqlCapture();
        var serverVersion = ServerVersion.AutoDetect(fixture.ConnectionString);
        var ctx = new TestDbContext(
            new DbContextOptionsBuilder<TestDbContext>()
                .UseMySql(fixture.ConnectionString, serverVersion)
                .UseLocking()
                .AddInterceptors(capture)
                .Options
        );
        return (ctx, capture);
    }

    protected override async Task ResetDatabaseAsync(TestDbContext ctx)
    {
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `OrderLines`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `Products`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `Categories`");
    }
}
