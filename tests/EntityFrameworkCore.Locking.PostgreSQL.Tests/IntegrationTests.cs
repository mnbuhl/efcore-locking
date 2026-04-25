using EntityFrameworkCore.Locking.PostgreSQL.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.PostgreSQL.Tests;

[Collection("Postgres")]
public partial class IntegrationTests(PostgresFixture fixture) : IntegrationTestsBase
{
    protected override TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseNpgsql(fixture.ConnectionString).UseLocking().Options);

    protected override (TestDbContext ctx, SqlCapture capture) CreateContextWithCapture()
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

    protected override Task ResetDatabaseAsync(TestDbContext ctx) =>
        ctx.Database.ExecuteSqlRawAsync("""TRUNCATE "OrderLines", "Products", "Categories" RESTART IDENTITY CASCADE""");
}
