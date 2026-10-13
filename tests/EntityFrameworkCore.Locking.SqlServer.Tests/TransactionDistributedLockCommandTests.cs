using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.SqlServer.Tests;

public class TransactionDistributedLockCommandTests
{
    private readonly SqlServerAdvisoryLockProvider _provider = new();

    [Theory]
    [InlineData(false, false, DistributedLockMode.Exclusive)]
    [InlineData(false, true, DistributedLockMode.Shared)]
    [InlineData(true, false, DistributedLockMode.Shared)]
    [InlineData(true, true, DistributedLockMode.Exclusive)]
    public async Task Commands_UseCallerTransactionAndNativeBudget(
        bool async,
        bool tryAcquire,
        DistributedLockMode mode
    )
    {
        using var connection = new RecordingConnection();
        using var context = CreateContext(connection);
        using var transaction = new RecordingTransaction(connection);
        context.Database.UseTransaction(transaction);
        await Invoke(context, async, tryAcquire, mode);

        var command = Assert.Single(connection.Commands);
        Assert.Equal("sp_getapplock", command.CommandText);
        Assert.Equal(CommandType.StoredProcedure, command.CommandType);
        Assert.Equal(0, command.CommandTimeout);
        Assert.Same(transaction, command.Transaction);
        Assert.Equal("key with spaces", command.Parameters["@Resource"].Value);
        Assert.Equal(mode.ToString(), command.Parameters["@LockMode"].Value);
        Assert.Equal("Transaction", command.Parameters["@LockOwner"].Value);
        Assert.Equal("public", command.Parameters["@DbPrincipal"].Value);
        Assert.Equal(tryAcquire ? 0 : 2, command.Parameters["@LockTimeout"].Value);
        Assert.Equal(ParameterDirection.ReturnValue, command.Parameters["@ReturnValue"].Direction);
        Assert.Equal(async ? 1 : 0, command.AsyncCalls);
        Assert.Equal(async ? 0 : 1, command.SyncCalls);
        Assert.True(command.WasDisposed);
    }

    [Fact]
    public async Task AmbientPath_LeavesCommandTransactionUnset()
    {
        using var connection = new RecordingConnection();
        using var context = CreateContext(connection);
        // The facade validates ambient enlistment; the provider must let the driver bind it.
        await _provider.AcquireAsync(
            context,
            connection,
            "key",
            null,
            default,
            DistributedLockMode.Exclusive,
            DistributedLockScope.Transaction
        );
        var command = Assert.Single(connection.Commands);
        Assert.Null(command.Transaction);
        Assert.Equal(-1, command.Parameters["@LockTimeout"].Value);
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 1)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 0)]
    public async Task SuccessReturnCodes_AcquireSuccessfully(bool async, bool tryAcquire, int code)
    {
        using var connection = new RecordingConnection { ReturnCode = code };
        using var context = CreateContext(connection);
        await Invoke(context, async, tryAcquire);
    }

    [Theory]
    [InlineData(false, false, -1, typeof(LockTimeoutException))]
    [InlineData(true, false, -1, typeof(LockTimeoutException))]
    [InlineData(false, false, -2, typeof(OperationCanceledException))]
    [InlineData(true, true, -2, typeof(OperationCanceledException))]
    [InlineData(false, true, -3, typeof(DeadlockException))]
    [InlineData(true, false, -3, typeof(DeadlockException))]
    [InlineData(false, true, -999, typeof(LockAcquisitionFailedException))]
    [InlineData(true, true, 2, typeof(LockAcquisitionFailedException))]
    [InlineData(true, true, null, typeof(LockAcquisitionFailedException))]
    public async Task FailureReturnCodes_ThrowTypedErrors(bool async, bool tryAcquire, int? code, Type exceptionType)
    {
        using var connection = new RecordingConnection { ReturnCode = code };
        using var context = CreateContext(connection);
        var error = await Record.ExceptionAsync(() => Invoke(context, async, tryAcquire));
        Assert.NotNull(error);
        Assert.Equal(exceptionType, error.GetType());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TryAcquire_OnlyTimeoutReturnsFalse(bool async)
    {
        using var connection = new RecordingConnection { ReturnCode = -1 };
        using var context = CreateContext(connection);
        var result = await Invoke(context, async, true, expected: false, key: "key");
        Assert.NotNull(result);
        Assert.False(result.IsAcquired);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreCanceledToken_DoesNotCreateCommand(bool tryAcquire)
    {
        using var connection = new RecordingConnection();
        using var context = CreateContext(connection);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Invoke(context, true, tryAcquire, ct: cancellation.Token)
        );
        Assert.Empty(connection.Commands);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DriverFailureAfterCancellation_ThrowsCancellationAndCancelsCommand(bool tryAcquire)
    {
        using var cancellation = new CancellationTokenSource();
        using var connection = new RecordingConnection
        {
            BeforeExecute = cancellation.Cancel,
            ExecuteException = ExceptionTranslationTests.CreateSqlException(0),
        };
        using var context = CreateContext(connection);
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Invoke(context, true, tryAcquire, ct: cancellation.Token)
        );
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(1, Assert.Single(connection.Commands).CancelCalls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RawDriverDeadlock_TranslatesInEveryPath(bool async, bool tryAcquire)
    {
        var nativeError = ExceptionTranslationTests.CreateSqlException(1205);
        using var connection = new RecordingConnection { ExecuteException = nativeError };
        using var context = CreateContext(connection);
        var error = await Assert.ThrowsAsync<DeadlockException>(() => Invoke(context, async, tryAcquire));
        Assert.Same(nativeError, error.InnerException);
    }

    [Fact]
    public async Task UnknownDriverError_IsNotOrdinaryContention()
    {
        var nativeError = ExceptionTranslationTests.CreateSqlException(2627);
        using var connection = new RecordingConnection { ExecuteException = nativeError };
        using var context = CreateContext(connection);
        var error = await Assert.ThrowsAsync<SqlException>(() => Invoke(context, true, true));
        Assert.Same(nativeError, error);
    }

    [Fact]
    public void InvalidMode_DoesNotCreateCommand()
    {
        using var connection = new RecordingConnection();
        using var context = CreateContext(connection);
        Assert.Throws<LockingConfigurationException>(() =>
            _provider.TryAcquire(context, connection, "key", (DistributedLockMode)999, DistributedLockScope.Transaction)
        );
        Assert.Empty(connection.Commands);
    }

    private static DbContext CreateContext(DbConnection connection) =>
        new(new DbContextOptionsBuilder().UseSqlServer(connection).Options);

    private async Task<IDistributedLockHandle?> Invoke(
        DbContext context,
        bool async,
        bool tryAcquire,
        DistributedLockMode mode = DistributedLockMode.Exclusive,
        CancellationToken ct = default,
        bool expected = true,
        string key = "key with spaces"
    )
    {
        var connection = context.Database.GetDbConnection();
        IDistributedLockHandle? result;
        if (tryAcquire)
        {
            result = async
                ? await _provider.TryAcquireAsync(context, connection, key, ct, mode, DistributedLockScope.Transaction)
                : _provider.TryAcquire(context, connection, key, mode, DistributedLockScope.Transaction);
            Assert.NotNull(result);
            Assert.Equal(expected, result.IsAcquired);
        }
        else
            result = async
                ? await _provider.AcquireAsync(
                    context,
                    connection,
                    key,
                    TimeSpan.FromTicks(10001),
                    ct,
                    mode,
                    DistributedLockScope.Transaction
                )
                : _provider.Acquire(
                    context,
                    connection,
                    key,
                    TimeSpan.FromTicks(10001),
                    mode,
                    DistributedLockScope.Transaction
                );
        return result;
    }

    private sealed class RecordingConnection : DbConnection
    {
        public List<RecordingCommand> Commands { get; } = [];
        public int? ReturnCode { get; init; } = 0;
        public Exception? ExecuteException { get; init; }
        public Action? BeforeExecute { get; init; }

        [AllowNull]
        public override string ConnectionString { get; set; } = "";
        public override string Database => "test";
        public override string DataSource => "test";
        public override string ServerVersion => "16";
        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

        public override void Close() => throw new NotSupportedException();

        public override void Open() => throw new NotSupportedException();

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();

        protected override DbCommand CreateDbCommand()
        {
            var command = new RecordingCommand(this);
            Commands.Add(command);
            return command;
        }
    }

    private sealed class RecordingTransaction(DbConnection connection) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        protected override DbConnection DbConnection => connection;

        public override void Commit() => throw new NotSupportedException();

        public override void Rollback() => throw new NotSupportedException();
    }

    private sealed class RecordingCommand(RecordingConnection connection) : DbCommand
    {
        private readonly SqlCommand _parameters = new();
        public int AsyncCalls { get; private set; }
        public int SyncCalls { get; private set; }
        public int CancelCalls { get; private set; }
        public bool WasDisposed { get; private set; }

        [AllowNull]
        public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; } = 30;
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = connection;
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => _parameters.Parameters;

        protected override DbParameter CreateDbParameter() => new SqlParameter();

        public override void Cancel() => CancelCalls++;

        public override void Prepare() => throw new NotSupportedException();

        public override object? ExecuteScalar() => throw new NotSupportedException();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            throw new NotSupportedException();

        public override int ExecuteNonQuery()
        {
            SyncCalls++;
            return Execute();
        }

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        {
            AsyncCalls++;
            return Task.FromResult(Execute());
        }

        private int Execute()
        {
            connection.BeforeExecute?.Invoke();
            if (connection.ExecuteException is { } error)
                throw error;
            Parameters["@ReturnValue"].Value = connection.ReturnCode;
            return 0;
        }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            // Keep recorded parameters readable for assertions after provider disposal.
            base.Dispose(disposing);
        }
    }
}
