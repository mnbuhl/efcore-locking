using EntityFrameworkCore.Locking.Oracle;
using EntityFrameworkCore.Locking.Oracle.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.Oracle.Tests;

[Collection("Oracle")]
public class IntegrationTests(OracleFixture fixture) : IntegrationTestsBase
{
    protected override TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseOracle(fixture.ConnectionString).UseLocking().Options);

    protected override (TestDbContext ctx, SqlCapture capture) CreateContextWithCapture()
    {
        var capture = new SqlCapture();
        var ctx = new TestDbContext(
            new DbContextOptionsBuilder<TestDbContext>()
                .UseOracle(fixture.ConnectionString)
                .UseLocking()
                .AddInterceptors(capture)
                .Options
        );
        return (ctx, capture);
    }

    protected override Task ResetDatabaseAsync(TestDbContext ctx) =>
        ctx.Database.ExecuteSqlRawAsync(
            """
            BEGIN
              EXECUTE IMMEDIATE 'DELETE FROM "OrderLines"';
              EXECUTE IMMEDIATE 'DELETE FROM "Products"';
              EXECUTE IMMEDIATE 'DELETE FROM "Categories"';
            END;
            """
        );
}
