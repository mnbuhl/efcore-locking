using EntityFrameworkCore.Locking.Oracle.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.Oracle.Tests;

[Collection("Oracle")]
public class DistributedLockIntegrationTests(OracleFixture fixture) : DistributedLockIntegrationTestsBase
{
    protected override TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseOracle(fixture.ConnectionString).UseLocking().Options);

    // DBMS_LOCK.REQUEST timeout is integer-second granularity.
    protected override TimeSpan DistributedLockAcquireTimeout => TimeSpan.FromSeconds(1);
}
