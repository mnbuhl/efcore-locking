using System.Diagnostics;
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
    [InlineData(false, "2s", 250)]
    [InlineData(true, "2s", 250)]
    [InlineData(false, "100ms", 5000)]
    [InlineData(true, "100ms", 5000)]
    public async Task SuccessfulAcquisition_RestoresExactCallerLockTimeout(
        bool sync,
        string callerTimeout,
        int requestedTimeoutMilliseconds
    )
    {
        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('lock_timeout', {callerTimeout}, true)");
        var previous = await ReadTimeout(context);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var key = $"timeout-setting-{Guid.NewGuid():N}";
            await Acquire(context, key, sync, timeout: TimeSpan.FromMilliseconds(requestedTimeoutMilliseconds));
            (await ReadTimeout(context)).Should().Be(previous);
        }
        await Acquire(context, $"null-timeout-{Guid.NewGuid():N}");
        (await ReadTimeout(context)).Should().Be(previous);
    }

    [Theory]
    [InlineData(false, DistributedLockMode.Exclusive, "100ms", 5000)]
    [InlineData(true, DistributedLockMode.Exclusive, "100ms", 5000)]
    [InlineData(false, DistributedLockMode.Shared, "100ms", 5000)]
    [InlineData(true, DistributedLockMode.Shared, "100ms", 5000)]
    [InlineData(false, DistributedLockMode.Exclusive, "0", 150)]
    [InlineData(true, DistributedLockMode.Exclusive, "0", 150)]
    public async Task ContendedAcquisition_UsesStricterFiniteTimeout(
        bool sync,
        DistributedLockMode mode,
        string callerTimeout,
        int requestedTimeoutMilliseconds
    )
    {
        await using var owner = CreateContext();
        await using var contender = CreateContext();
        await using var ownerTransaction = await owner.Database.BeginTransactionAsync();
        await using var contenderTransaction = await contender.Database.BeginTransactionAsync();
        await contender.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('lock_timeout', {callerTimeout}, true)"
        );
        // Bound the test even if a regression disables lock_timeout when the caller uses zero.
        await contender.Database.ExecuteSqlRawAsync("SET LOCAL statement_timeout = '10s'");
        var key = $"stricter-timeout-{Guid.NewGuid():N}";
        await Acquire(owner, key);
        var elapsed = Stopwatch.StartNew();

        await Assert.ThrowsAsync<LockTimeoutException>(() =>
            Acquire(contender, key, sync, mode, TimeSpan.FromMilliseconds(requestedTimeoutMilliseconds))
        );

        elapsed.Stop();
        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
        await contenderTransaction.RollbackAsync();
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
        await Acquire(owner, key);
        await Assert.ThrowsAsync<LockTimeoutException>(() =>
            Acquire(contender, key, timeout: TimeSpan.FromMilliseconds(100))
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
        await Acquire(owner, key);
        (await Try(contender, key)).IsAcquired.Should().BeFalse();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Acquire(contender, key, timeout: TimeSpan.FromSeconds(10), ct: cancellation.Token)
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
        var result = await Try(owner, key);
        result.IsAcquired.Should().BeTrue();
        (await Try(contender, key)).IsAcquired.Should().BeFalse();
        await ownerTransaction.RollbackToSavepointAsync("before_advisory");
        (await Try(contender, key)).IsAcquired.Should().BeTrue();
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
