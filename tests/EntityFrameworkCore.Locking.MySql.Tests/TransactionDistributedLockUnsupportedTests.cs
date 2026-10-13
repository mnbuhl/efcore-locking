using System.Data;
using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.MySql.Tests;

public class TransactionDistributedLockUnsupportedTests
{
    [Fact]
    public async Task TransactionDistributedLock_AllMethodsRejectWithoutOpeningMySqlConnection()
    {
        using var context = new DbContext(
            new DbContextOptionsBuilder<DbContext>()
                .UseMySql(
                    "Server=127.0.0.1;Database=unused;User=unused;Password=unused",
                    new MySqlServerVersion(new Version(8, 0, 36))
                )
                .UseLocking()
                .Options
        );
        var connection = context.Database.GetDbConnection();
        connection.State.Should().Be(ConnectionState.Closed);
        context.Database.SupportsTransactionDistributedLocks().Should().BeFalse();
        context.Database.SupportsDistributedLocks().Should().BeTrue();
        Assert.Throws<LockingConfigurationException>(() =>
            context.Database.AcquireDistributedLock("key", scope: DistributedLockScope.Transaction)
        );
        Assert.Throws<LockingConfigurationException>(() =>
            context.Database.TryAcquireDistributedLock("key", scope: DistributedLockScope.Transaction)
        );
        await Assert.ThrowsAsync<LockingConfigurationException>(() =>
            context.Database.AcquireDistributedLockAsync("key", scope: DistributedLockScope.Transaction)
        );
        await Assert.ThrowsAsync<LockingConfigurationException>(() =>
            context.Database.TryAcquireDistributedLockAsync("key", scope: DistributedLockScope.Transaction)
        );
        connection.State.Should().Be(ConnectionState.Closed);
    }
}
