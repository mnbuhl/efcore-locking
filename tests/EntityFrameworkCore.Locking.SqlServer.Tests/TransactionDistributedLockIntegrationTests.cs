using EntityFrameworkCore.Locking.SqlServer.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.SqlServer.Tests;

[Collection("SqlServer")]
public class TransactionDistributedLockIntegrationTests(SqlServerFixture fixture)
    : TransactionDistributedLockIntegrationTestsBase
{
    protected override TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseSqlServer(fixture.ConnectionString).UseLocking().Options);
}
