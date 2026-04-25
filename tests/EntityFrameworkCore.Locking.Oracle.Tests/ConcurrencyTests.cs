using EntityFrameworkCore.Locking.Oracle.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.Oracle.Tests;

[Collection("Oracle")]
public class ConcurrencyTests(OracleFixture fixture) : ConcurrencyTestsBase
{
    protected override TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseOracle(fixture.ConnectionString).UseLocking().Options);

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

    // Oracle FOR UPDATE WAIT takes integer seconds — sub-second timeouts round up to 1s.
    protected override TimeSpan WaitTimeout => TimeSpan.FromSeconds(1);
}
