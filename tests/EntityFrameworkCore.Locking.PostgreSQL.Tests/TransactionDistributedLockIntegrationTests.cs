using System.Globalization;
using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.PostgreSQL.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace EntityFrameworkCore.Locking.PostgreSQL.Tests;

[Collection("Postgres")]
public class TransactionDistributedLockIntegrationTests(PostgresFixture fixture)
    : TransactionDistributedLockIntegrationTestsBase
{
    protected override TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseNpgsql(fixture.ConnectionString).UseLocking().Options);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulAcquisition_RestoresExactCallerLockTimeout(bool sync)
    {
        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '2s'");
        var previous = await ReadTimeout(context);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var key = $"timeout-setting-{Guid.NewGuid():N}";
            if (sync)
                context.Database.AcquireTransactionDistributedLock(key, TimeSpan.FromMilliseconds(250));
            else
                await context.Database.AcquireTransactionDistributedLockAsync(key, TimeSpan.FromMilliseconds(250));
            (await ReadTimeout(context)).Should().Be(previous);
        }
        await context.Database.AcquireTransactionDistributedLockAsync($"null-timeout-{Guid.NewGuid():N}");
        (await ReadTimeout(context)).Should().Be(previous);
    }

    [Fact]
    public async Task FailedAcquisition_PreservesTimeoutError_AndCallerRollbackRestoresSetting()
    {
        await using var owner = CreateContext();
        await using var contender = CreateContext();
        await using var ownerTransaction = await owner.Database.BeginTransactionAsync();
        await contender.Database.OpenConnectionAsync();
        var sessionSetting = await ReadTimeout(contender);
        await using var contenderTransaction = await contender.Database.BeginTransactionAsync();
        await contender.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '2s'");
        var key = $"failed-setting-{Guid.NewGuid():N}";
        await owner.Database.AcquireTransactionDistributedLockAsync(key);
        await Assert.ThrowsAsync<LockTimeoutException>(() =>
            contender.Database.AcquireTransactionDistributedLockAsync(key, TimeSpan.FromMilliseconds(100))
        );
        await contenderTransaction.RollbackAsync();
        (await ReadTimeout(contender)).Should().Be(sessionSetting);
    }

    [Fact]
    public async Task Cancellation_PreservesError_AndCallerRollbackRestoresTimeoutSetting()
    {
        await using var owner = CreateContext();
        await using var contender = CreateContext();
        await using var ownerTransaction = await owner.Database.BeginTransactionAsync();
        await contender.Database.OpenConnectionAsync();
        var sessionSetting = await ReadTimeout(contender);
        await using var contenderTransaction = await contender.Database.BeginTransactionAsync();
        await contender.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '2s'");
        var key = $"canceled-setting-{Guid.NewGuid():N}";
        await owner.Database.AcquireTransactionDistributedLockAsync(key);
        (await contender.Database.TryAcquireTransactionDistributedLockAsync(key)).IsAcquired.Should().BeFalse();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            contender
                .Database.AcquireTransactionDistributedLockAsync(key, TimeSpan.FromSeconds(10), cancellation.Token)
                .WaitAsync(TimeSpan.FromSeconds(15))
        );
        await contenderTransaction.RollbackAsync();
        (await ReadTimeout(contender)).Should().Be(sessionSetting);
    }

    [Fact]
    public async Task CallerSavepointRollback_ReleasesLockAcquiredAfterSavepoint()
    {
        await using var owner = CreateContext();
        await using var contender = CreateContext();
        await using var ownerTransaction = await owner.Database.BeginTransactionAsync();
        await using var contenderTransaction = await contender.Database.BeginTransactionAsync();
        await ownerTransaction.CreateSavepointAsync("before_advisory");
        var key = $"savepoint-{Guid.NewGuid():N}";
        var result = await owner.Database.TryAcquireTransactionDistributedLockAsync(key);
        result.IsAcquired.Should().BeTrue();
        (await contender.Database.TryAcquireTransactionDistributedLockAsync(key)).IsAcquired.Should().BeFalse();
        await ownerTransaction.RollbackToSavepointAsync("before_advisory");
        (await contender.Database.TryAcquireTransactionDistributedLockAsync(key)).IsAcquired.Should().BeTrue();
        result.IsAcquired.Should().BeTrue("it is an outcome, not current ownership");
    }

    private static async Task<string> ReadTimeout(TestDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SHOW lock_timeout";
        return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException("PostgreSQL must expose lock_timeout.");
    }
}
