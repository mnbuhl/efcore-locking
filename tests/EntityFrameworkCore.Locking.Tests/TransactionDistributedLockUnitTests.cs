using System.Data;
using System.Data.Common;
using System.Transactions;
using AwesomeAssertions;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;
using AmbientTransaction = System.Transactions.Transaction;

namespace EntityFrameworkCore.Locking.Tests;

public class TransactionDistributedLockUnitTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public async Task DistributedLock_DefaultAndExplicitSessionScopeUseSessionProviderInsideTransaction(
        int operation,
        bool explicitScope
    )
    {
        using var context = CreateContext();
        using var transaction = context.Database.BeginTransaction();
        IDistributedLockHandle? handle = operation switch
        {
            0 => explicitScope
                ? context.Database.AcquireDistributedLock("session", scope: DistributedLockScope.Session)
                : context.Database.AcquireDistributedLock("session"),
            1 => explicitScope
                ? context.Database.TryAcquireDistributedLock("session", scope: DistributedLockScope.Session)
                : context.Database.TryAcquireDistributedLock("session"),
            2 => explicitScope
                ? await context.Database.AcquireDistributedLockAsync("session", scope: DistributedLockScope.Session)
                : await context.Database.AcquireDistributedLockAsync("session"),
            _ => explicitScope
                ? await context.Database.TryAcquireDistributedLockAsync("session", scope: DistributedLockScope.Session)
                : await context.Database.TryAcquireDistributedLockAsync("session"),
        };
        Assert.NotNull(handle);
        handle.IsAcquired.Should().BeTrue();
        context.Provider.Transaction.Calls.Should().BeEmpty();
        handle.Dispose();
        // Real session disposal releases the registry entry and the provider lock.
        using var reacquired = context.Database.TryAcquireDistributedLock("session");
        Assert.NotNull(reacquired);
        reacquired.IsAcquired.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task DistributedLock_InvalidScopeRejectsBeforeProviderOrConnectionLifecycle(int operation)
    {
        using var context = CreateContext(new TransactionTestConnection(ConnectionState.Closed));
        var connection = (TransactionTestConnection)context.Database.GetDbConnection();
        var before = connection.Lifecycle;
        var invalid = (DistributedLockScope)99;
        await Assert.ThrowsAsync<LockingConfigurationException>(() => Invoke(context, operation, scope: invalid));
        context.Provider.Transaction.Calls.Should().BeEmpty();
        connection.Lifecycle.Should().Be(before);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TransactionDistributedLock_DisposalAndReleaseAreNoOpsWithImmutableOutcome(bool acquired)
    {
        using var context = CreateContext();
        using var transaction = context.Database.BeginTransaction();
        context.Provider.Transaction.Outcome = acquired;
        var result = context.Database.TryAcquireDistributedLock("key", scope: DistributedLockScope.Transaction);
        Assert.NotNull(result);
        var connection = (TransactionTestConnection)context.Database.GetDbConnection();
        var before = connection.Lifecycle;
        result.Release();
        await result.ReleaseAsync();
        result.Dispose();
        await result.DisposeAsync();
        result.Release();
        result.Dispose();
        connection.Lifecycle.Should().Be(before);
        context.Provider.Transaction.Calls.Should().ContainSingle();
        result.IsAcquired.Should().Be(acquired);
        transaction.Rollback();
        result.IsAcquired.Should().Be(acquired);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task TransactionDistributedLock_AllMethods_DispatchWithoutChangingCallerLifecycle(int operation)
    {
        using var context = CreateContext();
        using var transaction = context.Database.BeginTransaction();
        var connection = (TransactionTestConnection)context.Database.GetDbConnection();
        var before = connection.Lifecycle;
        var provider = context.Provider.Transaction;
        await Invoke(context, operation, new string('k', 255), DistributedLockMode.Shared);
        provider.Calls.Should().Equal(operation);
        provider.LastKey.Should().Be(new string('k', 255));
        provider.LastTransactionMode.Should().Be(DistributedLockMode.Shared);
        connection.Lifecycle.Should().Be(before);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TransactionDistributedLock_ResultIsAnImmutableOutcomeAfterCommit(bool acquired)
    {
        using var context = CreateContext();
        context.Provider.Transaction.Outcome = acquired;
        using var transaction = context.Database.BeginTransaction();
        var sync = context.Database.TryAcquireDistributedLock("sync", scope: DistributedLockScope.Transaction);
        var asyncResult = await context.Database.TryAcquireDistributedLockAsync(
            "async",
            scope: DistributedLockScope.Transaction
        );
        transaction.Commit();
        Assert.NotNull(sync);
        Assert.NotNull(asyncResult);
        sync.IsAcquired.Should().Be(acquired);
        asyncResult.IsAcquired.Should().Be(acquired);
        sync.Key.Should().Be("sync");
        asyncResult.Key.Should().Be("async");
        typeof(TransactionDistributedLockResult).GetProperties().Should().OnlyContain(property => !property.CanWrite);
    }

    [Fact]
    public async Task TransactionDistributedLock_RepeatedAndStrengthenedAcquisitionsBypassSessionRegistry()
    {
        using var context = CreateContext();
        using var transaction = context.Database.BeginTransaction();
        await Invoke(context, 0, "repeat", DistributedLockMode.Shared);
        await Invoke(context, 2, "repeat", DistributedLockMode.Exclusive);
        var sync = await Invoke(context, 1, "repeat");
        var asyncResult = await Invoke(context, 3, "repeat");
        Assert.NotNull(sync);
        Assert.NotNull(asyncResult);
        sync.IsAcquired.Should().BeTrue();
        asyncResult.IsAcquired.Should().BeTrue();
        // A session acquisition must still reach its provider rather than see a transaction registry entry.
        using var session = context.Database.AcquireDistributedLock("repeat", mode: DistributedLockMode.Shared);
        context.Provider.Transaction.Calls.Should().HaveCount(4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task TransactionDistributedLock_InvalidSetupMakesNoAcquisitionOrLifecycleCalls(int operation)
    {
        foreach (
            var invalid in new[]
            {
                "closed",
                "missing",
                "null-key",
                "empty-key",
                "long-key",
                "mode",
                "negative",
                "disposed",
                "completed",
            }
        )
        {
            using var context = CreateContext();
            var connection = (TransactionTestConnection)context.Database.GetDbConnection();
            using var transaction = invalid is "closed" or "missing" ? null : context.Database.BeginTransaction();
            if (invalid == "closed")
                connection.Close();
            if (invalid == "disposed")
                transaction!.GetDbTransaction().Dispose();
            if (invalid == "completed")
                transaction!.Commit();
            var before = connection.Lifecycle;
            var key = invalid switch
            {
                "null-key" => null!,
                "empty-key" => "",
                "long-key" => new string('x', 256),
                _ => "key",
            };
            // Try methods have no timeout; negative timeout is covered by Acquire only.
            if (invalid == "negative" && operation is 1 or 3)
                continue;
            await Assert.ThrowsAsync<LockingConfigurationException>(() =>
                Invoke(
                    context,
                    operation,
                    key,
                    invalid == "mode" ? (DistributedLockMode)99 : DistributedLockMode.Exclusive,
                    invalid == "negative" ? TimeSpan.FromTicks(-1) : null
                )
            );
            context.Provider.Transaction.Calls.Should().BeEmpty(invalid);
            connection.Lifecycle.Should().Be(before, invalid);
        }
    }

    [Fact]
    public async Task TransactionDistributedLock_ExternallyOwnedTransactionMustRemainAttachedAndActive()
    {
        var connection = new TransactionTestConnection();
        using var context = CreateContext(connection);
        using var external = connection.BeginTransaction();
        using (context.Database.UseTransaction(external))
        {
            var before = connection.Lifecycle;
            for (var operation = 0; operation < 4; operation++)
                await Invoke(context, operation);
            connection.Lifecycle.Should().Be(before);
        }
        context.Database.CurrentTransaction.Should().BeNull();
        external.Connection.Should().BeSameAs(connection);
        var afterDetach = connection.Lifecycle;
        await Assert.ThrowsAsync<LockingConfigurationException>(() => Invoke(context, 2));
        connection.Lifecycle.Should().Be(afterDetach);
        context.Provider.Transaction.Calls.Should().HaveCount(4);
    }

    [Fact]
    public async Task TransactionDistributedLock_ExplicitTransactionWithDifferentConnectionIsRejected()
    {
        using var context = CreateContext();
        using var transaction = context.Database.BeginTransaction();
        var fake = (TransactionTestDbTransaction)transaction.GetDbTransaction();
        fake.ReportedConnection = new TransactionTestConnection();
        var connection = (TransactionTestConnection)context.Database.GetDbConnection();
        var before = connection.Lifecycle;
        for (var operation = 0; operation < 4; operation++)
            await Assert.ThrowsAsync<LockingConfigurationException>(() => Invoke(context, operation));
        connection.Lifecycle.Should().Be(before);
        context.Provider.Transaction.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task TransactionDistributedLock_PreCanceledAsyncCallsNeverReachProvider()
    {
        using var context = CreateContext();
        using var transaction = context.Database.BeginTransaction();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Invoke(context, 2, ct: cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Invoke(context, 3, ct: cancellation.Token));
        context.Provider.Transaction.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task TransactionDistributedLock_ExplicitEfEnlistedAmbientTransactionIsAcceptedWithoutMutation()
    {
        using var ambient = new System.Transactions.CommittableTransaction();
        var previous = AmbientTransaction.Current;
        try
        {
            AmbientTransaction.Current = ambient;
            using var context = CreateContext();
            context.Database.EnlistTransaction(ambient);
            context.Database.CurrentTransaction.Should().BeNull();
            var connection = (TransactionTestConnection)context.Database.GetDbConnection();
            var before = connection.Lifecycle;
            for (var operation = 0; operation < 4; operation++)
                await Invoke(context, operation);
            connection.Lifecycle.Should().Be(before);
            context.Provider.Transaction.Calls.Should().Equal(0, 1, 2, 3);
        }
        finally
        {
            AmbientTransaction.Current = previous;
        }
    }

    [Fact]
    public async Task TransactionDistributedLock_CompletedScopeRejectsBeforeScopeDisposal()
    {
        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        using var context = CreateContext();
        context.Database.EnlistTransaction(AmbientTransaction.Current);
        var connection = (TransactionTestConnection)context.Database.GetDbConnection();
        scope.Complete();
        var before = connection.Lifecycle;
        for (var operation = 0; operation < 4; operation++)
            await Assert.ThrowsAsync<LockingConfigurationException>(() => Invoke(context, operation));
        context.Provider.Transaction.Calls.Should().BeEmpty();
        connection.Lifecycle.Should().Be(before);
    }

    [Theory]
    [InlineData("untracked")]
    [InlineData("suppressed")]
    [InlineData("mismatched")]
    [InlineData("completed")]
    [InlineData("disposed")]
    public async Task TransactionDistributedLock_InvalidAmbientTrackingRejectsWithoutMutation(string scenario)
    {
        using var ambient = new System.Transactions.CommittableTransaction();
        using var other = new System.Transactions.CommittableTransaction();
        var previous = AmbientTransaction.Current;
        try
        {
            AmbientTransaction.Current = ambient;
            using var context = CreateContext();
            if (scenario != "untracked")
                context.Database.EnlistTransaction(ambient);
            if (scenario == "suppressed")
                AmbientTransaction.Current = null;
            if (scenario == "mismatched")
                AmbientTransaction.Current = other;
            if (scenario == "completed")
                ambient.Commit();
            if (scenario == "disposed")
                ambient.Dispose();
            var connection = (TransactionTestConnection)context.Database.GetDbConnection();
            var before = connection.Lifecycle;
            for (var operation = 0; operation < 4; operation++)
                await Assert.ThrowsAsync<LockingConfigurationException>(() => Invoke(context, operation));
            context.Provider.Transaction.Calls.Should().BeEmpty();
            connection.Lifecycle.Should().Be(before);
        }
        finally
        {
            AmbientTransaction.Current = previous;
        }
    }

    [Fact]
    public async Task TransactionDistributedLock_LegacyProviderAndMissingRegistrationAreUnsupported()
    {
        foreach (var registered in new[] { true, false })
        {
            var connection = new TransactionTestConnection(ConnectionState.Closed);
            var options = new DbContextOptionsBuilder<DbContext>()
                .UseSqlServer(connection)
                .EnableServiceProviderCaching(false)
                .Options;
            if (registered)
                options =
                    (DbContextOptions<DbContext>)
                        options.WithExtension(new LockingOptionsExtension(new FakeLockingProvider()));
            using var context = new DbContext(options);
            context.Database.SupportsTransactionDistributedLocks().Should().BeFalse();
            if (registered)
                context.Database.SupportsDistributedLocks().Should().BeTrue();
            for (var operation = 0; operation < 4; operation++)
                await Assert.ThrowsAsync<LockingConfigurationException>(() => Invoke(context, operation));
            connection.Lifecycle.Should().Be((0, 0, 0, 0, 0, 0));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task AdvisoryLockProvider_LegacyImplementationDelegatesSessionAndRejectsTransaction(int operation)
    {
        using var context = CreateContext(new TransactionTestConnection(ConnectionState.Closed));
        var connection = (TransactionTestConnection)context.Database.GetDbConnection();
        IAdvisoryLockProvider provider = new FakeAdvisoryLockProvider();
        provider.SupportsTransactionLocks.Should().BeFalse();
        var before = connection.Lifecycle;

        async Task<IDistributedLockHandle?> Acquire(DistributedLockScope scope) =>
            operation switch
            {
                0 => provider.Acquire(context, connection, "legacy", null, DistributedLockMode.Exclusive, scope),
                1 => provider.TryAcquire(context, connection, "legacy", DistributedLockMode.Exclusive, scope),
                2 => await provider.AcquireAsync(
                    context,
                    connection,
                    "legacy",
                    null,
                    default,
                    DistributedLockMode.Exclusive,
                    scope
                ),
                _ => await provider.TryAcquireAsync(
                    context,
                    connection,
                    "legacy",
                    default,
                    DistributedLockMode.Exclusive,
                    scope
                ),
            };

        using var handle = await Acquire(DistributedLockScope.Session);
        Assert.NotNull(handle);
        handle.IsAcquired.Should().BeTrue();
        await Assert.ThrowsAsync<LockingConfigurationException>(() => Acquire(DistributedLockScope.Transaction));
        connection.Lifecycle.Should().Be(before);
    }

    [Fact]
    public void TransactionDistributedLock_CapabilityIsIndependentOfConnectionAndTransactionReadiness()
    {
        using var context = CreateContext(new TransactionTestConnection(ConnectionState.Closed));
        context.Database.SupportsTransactionDistributedLocks().Should().BeTrue();
        ((TransactionTestConnection)context.Database.GetDbConnection()).Lifecycle.Should().Be((0, 0, 0, 0, 0, 0));
    }

    [Fact]
    public void TransactionDistributedLock_TimeoutConversionPreservesNoWaitAndClampsFiniteBudgets()
    {
        TransactionDistributedLockTimeout.ToMilliseconds(null).Should().Be(-1);
        TransactionDistributedLockTimeout.ToMilliseconds(TimeSpan.Zero).Should().Be(0);
        TransactionDistributedLockTimeout.ToMilliseconds(TimeSpan.FromTicks(1)).Should().Be(1);
        TransactionDistributedLockTimeout.ToMilliseconds(TimeSpan.FromMilliseconds(1.1)).Should().Be(2);
        TransactionDistributedLockTimeout.ToMilliseconds(TimeSpan.MaxValue).Should().Be(int.MaxValue);
        Assert.Throws<LockingConfigurationException>(() =>
            TransactionDistributedLockTimeout.ToMilliseconds(TimeSpan.FromTicks(-1))
        );
    }

    private static async Task<IDistributedLockHandle?> Invoke(
        DbContext context,
        int operation,
        string key = "key",
        DistributedLockMode mode = DistributedLockMode.Exclusive,
        TimeSpan? timeout = null,
        CancellationToken ct = default,
        DistributedLockScope scope = DistributedLockScope.Transaction
    ) =>
        operation switch
        {
            0 => context.Database.AcquireDistributedLock(key, timeout, mode, scope: scope),
            1 => context.Database.TryAcquireDistributedLock(key, mode, scope: scope),
            2 => await context.Database.AcquireDistributedLockAsync(key, timeout, ct, mode, scope),
            3 => await context.Database.TryAcquireDistributedLockAsync(key, ct, mode, scope),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    private static TransactionTestContext CreateContext(TransactionTestConnection? connection = null)
    {
        var provider = new TransactionTestRootProvider();
        var options = new DbContextOptionsBuilder<TransactionTestContext>()
            .UseSqlServer(connection ?? new TransactionTestConnection())
            .EnableServiceProviderCaching(false)
            .Options;
        options =
            (DbContextOptions<TransactionTestContext>)options.WithExtension(new LockingOptionsExtension(provider));
        return new TransactionTestContext(options, provider);
    }
}

internal sealed class TransactionTestContext(
    DbContextOptions<TransactionTestContext> options,
    TransactionTestRootProvider provider
) : DbContext(options)
{
    public TransactionTestRootProvider Provider { get; } = provider;
}

internal sealed class TransactionTestRootProvider : ILockingProvider
{
    public TransactionTestProvider Transaction { get; } = new();
    public IAdvisoryLockProvider AdvisoryLockProvider => Transaction;
    public ILockSqlGenerator RowLockGenerator { get; } = new FakeLockSqlGenerator();
    public IExceptionTranslator ExceptionTranslator { get; } = new FakeExceptionTranslator();
    public string ProviderName => "TransactionTest";
}

internal sealed class TransactionTestProvider : FakeAdvisoryLockProvider, IAdvisoryLockProvider
{
    public bool SupportsTransactionLocks => true;
    public List<int> Calls { get; } = [];
    public bool Outcome { get; set; } = true;
    public string? LastKey { get; private set; }
    public DistributedLockMode LastTransactionMode { get; private set; }

    private IDistributedLockHandle Record(int call, string key, DistributedLockMode mode, bool outcome)
    {
        Calls.Add(call);
        LastKey = key;
        LastTransactionMode = mode;
        return new TransactionDistributedLockResult(key, outcome);
    }

    public IDistributedLockHandle Acquire(
        DbContext context,
        DbConnection connection,
        string key,
        TimeSpan? timeout,
        DistributedLockMode mode,
        DistributedLockScope scope
    ) =>
        scope == DistributedLockScope.Session
            ? Acquire(context, connection, key, timeout, mode)
            : Record(0, key, mode, true);

    public IDistributedLockHandle? TryAcquire(
        DbContext context,
        DbConnection connection,
        string key,
        DistributedLockMode mode,
        DistributedLockScope scope
    ) =>
        scope == DistributedLockScope.Session
            ? TryAcquire(context, connection, key, mode)
            : Record(1, key, mode, Outcome);

    public Task<IDistributedLockHandle> AcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        TimeSpan? timeout,
        CancellationToken ct,
        DistributedLockMode mode,
        DistributedLockScope scope
    ) =>
        scope == DistributedLockScope.Session
            ? AcquireAsync(context, connection, key, timeout, ct, mode)
            : Task.FromResult(Record(2, key, mode, true));

    public Task<IDistributedLockHandle?> TryAcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        CancellationToken ct,
        DistributedLockMode mode,
        DistributedLockScope scope
    ) =>
        scope == DistributedLockScope.Session
            ? TryAcquireAsync(context, connection, key, ct, mode)
            : Task.FromResult<IDistributedLockHandle?>(Record(3, key, mode, Outcome));
}

internal sealed class TransactionTestConnection(ConnectionState state = ConnectionState.Open) : DbConnection
{
    private ConnectionState _state = state;

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ConnectionString { get; set; } = "Fake";
    public override string Database => "Fake";
    public override string DataSource => "Fake";
    public override string ServerVersion => "0.0";
    public override ConnectionState State => _state;
    public int OpenCount,
        CloseCount,
        EnlistCount,
        BeginCount,
        CommitCount,
        RollbackCount;
    public (int, int, int, int, int, int) Lifecycle =>
        (OpenCount, CloseCount, EnlistCount, BeginCount, CommitCount, RollbackCount);

    public override void Open()
    {
        OpenCount++;
        _state = ConnectionState.Open;
    }

    public override void Close()
    {
        CloseCount++;
        _state = ConnectionState.Closed;
    }

    public override void EnlistTransaction(AmbientTransaction? transaction) => EnlistCount++;

    protected override DbTransaction BeginDbTransaction(System.Data.IsolationLevel isolationLevel)
    {
        BeginCount++;
        return new TransactionTestDbTransaction(this, isolationLevel);
    }

    public override void ChangeDatabase(string databaseName) { }

    protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
}

internal sealed class TransactionTestDbTransaction(
    TransactionTestConnection connection,
    System.Data.IsolationLevel isolationLevel
) : DbTransaction
{
    private bool _completed;
    public DbConnection ReportedConnection { get; set; } = connection;
    public override System.Data.IsolationLevel IsolationLevel => isolationLevel;
    protected override DbConnection? DbConnection => _completed ? null : ReportedConnection;

    public override void Commit()
    {
        connection.CommitCount++;
        _completed = true;
    }

    public override void Rollback()
    {
        connection.RollbackCount++;
        _completed = true;
    }

    protected override void Dispose(bool disposing)
    {
        _completed = true;
        base.Dispose(disposing);
    }
}
