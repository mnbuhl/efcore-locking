using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace EntityFrameworkCore.Locking.PostgreSQL.Tests;

public class TransactionDistributedLockCommandTests
{
    private readonly PostgresAdvisoryLockProvider _provider = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Finite_acquisition_restores_exact_setting_and_binds_explicit_transaction(bool async)
    {
        using var connection = new RecordingConnection();
        using var context = CreateContext(connection);
        using var transaction = connection.BeginTransaction();
        context.Database.UseTransaction(transaction);
        connection.Results.Enqueue(() => "7s");
        connection.Results.Enqueue(() => "1ms");
        connection.Results.Enqueue(() => null);
        connection.Results.Enqueue(() => "7s");

        await Acquire(async, context, TimeSpan.FromTicks(1), mode: DistributedLockMode.Shared);

        Assert.Equal(4, connection.Commands.Count);
        Assert.All(connection.Commands, command => Assert.Same(transaction, command.Transaction));
        Assert.Equal("SELECT current_setting('lock_timeout')", connection.Commands[0].Sql);
        Assert.Equal(1, connection.Commands[1].Values[0]);
        Assert.Equal("SELECT pg_advisory_xact_lock_shared($1)", connection.Commands[2].Sql);
        Assert.Equal(PostgresAdvisoryLockProvider.ComputeKey("resource"), connection.Commands[2].Values[0]);
        Assert.Equal("7s", connection.Commands[3].Values[0]);
        Assert.Equal(0, connection.Commands[2].Timeout);
        Assert.All(
            connection.Commands.Where(command => !command.Sql.Contains("advisory", StringComparison.Ordinal)),
            command => Assert.True(command.Timeout > 0)
        );
    }

    [Fact]
    public async Task Cancellation_after_setting_does_not_dispatch_acquisition_or_restore()
    {
        using var connection = new RecordingConnection();
        using var context = CreateContext(connection);
        using var cancellation = new CancellationTokenSource();
        connection.Results.Enqueue(() => "5s");
        connection.Results.Enqueue(() =>
        {
            cancellation.Cancel();
            return "25ms";
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Acquire(true, context, TimeSpan.FromMilliseconds(25), ct: cancellation.Token)
        );

        Assert.Equal(2, connection.Commands.Count);
        Assert.DoesNotContain(
            connection.Commands,
            command => command.Sql.Contains("advisory", StringComparison.Ordinal)
        );
    }

    [Theory]
    [InlineData("55P03", typeof(LockTimeoutException))]
    [InlineData("57014", typeof(LockTimeoutException))]
    [InlineData("40P01", typeof(DeadlockException))]
    [InlineData("23505", typeof(LockAcquisitionFailedException))]
    public async Task Failed_acquisition_preserves_error_without_restoration(string state, Type expected)
    {
        using var connection = new RecordingConnection();
        using var context = CreateContext(connection);
        var failure = new PostgresException("failure", "ERROR", "ERROR", state);
        connection.Results.Enqueue(() => "5s");
        connection.Results.Enqueue(() => "25ms");
        connection.Results.Enqueue(() => throw failure);

        var error = await Record.ExceptionAsync(() => Acquire(true, context, TimeSpan.FromMilliseconds(25)));

        Assert.IsType(expected, error);
        Assert.Same(failure, error.InnerException);
        Assert.Equal(3, connection.Commands.Count);
    }

    [Fact]
    public async Task Successful_acquisition_restores_with_independent_bounded_token_after_caller_cancels()
    {
        using var connection = new RecordingConnection();
        using var context = CreateContext(connection);
        using var cancellation = new CancellationTokenSource();
        connection.Results.Enqueue(() => "5s");
        connection.Results.Enqueue(() => "25ms");
        connection.Results.Enqueue(() =>
        {
            cancellation.Cancel();
            return null;
        });
        connection.Results.Enqueue(() => "5s");

        await Acquire(true, context, TimeSpan.FromMilliseconds(25), ct: cancellation.Token);

        var cleanup = connection.Commands[3];
        Assert.True(cleanup.Token.CanBeCanceled);
        Assert.False(cleanup.Token.IsCancellationRequested);
        Assert.NotEqual(cancellation.Token, cleanup.Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Null_timeout_executes_only_native_acquisition_without_ambient_command_transaction(bool async)
    {
        using var connection = new RecordingConnection();
        using var context = CreateContext(connection);
        connection.Results.Enqueue(() => null);
        await Acquire(async, context, null);
        var command = Assert.Single(connection.Commands);
        Assert.Equal("SELECT pg_advisory_xact_lock($1)", command.Sql);
        Assert.Equal(0, command.Timeout);
        Assert.Null(command.Transaction);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Try_accepts_only_boolean_outcome(bool async, bool outcome)
    {
        using var connection = new RecordingConnection();
        using var context = CreateContext(connection);
        connection.Results.Enqueue(() => outcome);
        var result = await TryAcquire(async, context, mode: DistributedLockMode.Shared);
        Assert.NotNull(result);
        Assert.Equal(outcome, result.IsAcquired);
        var command = Assert.Single(connection.Commands);
        Assert.Equal("SELECT pg_try_advisory_xact_lock_shared($1)", command.Sql);
        Assert.Equal(0, command.Timeout);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData("true")]
    public async Task Malformed_try_result_is_a_failure(object? result)
    {
        using var connection = new RecordingConnection();
        using var context = CreateContext(connection);
        connection.Results.Enqueue(() => result);
        await Assert.ThrowsAsync<LockAcquisitionFailedException>(() => TryAcquire(true, context));
        connection.Results.Enqueue(() => result);
        Assert.Throws<LockAcquisitionFailedException>(() =>
        {
            _ = TryAcquire(false, context);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Zero_timeout_tries_once_and_throws_on_contention(bool async)
    {
        using var connection = new RecordingConnection();
        using var context = CreateContext(connection);
        connection.Results.Enqueue(() => false);
        if (async)
            await Assert.ThrowsAsync<LockTimeoutException>(() => Acquire(true, context, TimeSpan.Zero));
        else
            Assert.Throws<LockTimeoutException>(() =>
            {
                _ = Acquire(false, context, TimeSpan.Zero);
            });
        Assert.Equal("SELECT pg_try_advisory_xact_lock($1)", Assert.Single(connection.Commands).Sql);
    }

    [Fact]
    public async Task Restore_failure_is_reported_with_caller_rollback_requirement()
    {
        using var connection = new RecordingConnection();
        using var context = CreateContext(connection);
        var failure = new InvalidOperationException("restore failure");
        connection.Results.Enqueue(() => "5s");
        connection.Results.Enqueue(() => "25ms");
        connection.Results.Enqueue(() => null);
        connection.Results.Enqueue(() => throw failure);
        var error = await Assert.ThrowsAsync<LockAcquisitionFailedException>(() =>
            Acquire(true, context, TimeSpan.FromMilliseconds(25))
        );
        Assert.Same(failure, error.InnerException);
        Assert.Contains("roll back", error.Message, StringComparison.Ordinal);
    }

    private Task<IDistributedLockHandle> Acquire(
        bool async,
        DbContext context,
        TimeSpan? timeout,
        DistributedLockMode mode = DistributedLockMode.Exclusive,
        CancellationToken ct = default
    ) =>
        async
            ? _provider.AcquireAsync(
                context,
                context.Database.GetDbConnection(),
                "resource",
                timeout,
                ct,
                mode,
                DistributedLockScope.Transaction
            )
            : Task.FromResult(
                _provider.Acquire(
                    context,
                    context.Database.GetDbConnection(),
                    "resource",
                    timeout,
                    mode,
                    DistributedLockScope.Transaction
                )
            );

    private Task<IDistributedLockHandle?> TryAcquire(
        bool async,
        DbContext context,
        DistributedLockMode mode = DistributedLockMode.Exclusive,
        CancellationToken ct = default
    ) =>
        async
            ? _provider.TryAcquireAsync(
                context,
                context.Database.GetDbConnection(),
                "resource",
                ct,
                mode,
                DistributedLockScope.Transaction
            )
            : Task.FromResult(
                _provider.TryAcquire(
                    context,
                    context.Database.GetDbConnection(),
                    "resource",
                    mode,
                    DistributedLockScope.Transaction
                )
            );

    private static DbContext CreateContext(DbConnection connection) =>
        new(new DbContextOptionsBuilder().UseNpgsql(connection).Options);

    private sealed record CommandRecord(
        string Sql,
        int Timeout,
        DbTransaction? Transaction,
        object?[] Values,
        CancellationToken Token
    );

    private sealed class RecordingConnection : DbConnection
    {
        public Queue<Func<object?>> Results { get; } = new();
        public List<CommandRecord> Commands { get; } = new();

        [AllowNull]
        public override string ConnectionString { get; set; } = "";
        public override string Database => "recording";
        public override string DataSource => "recording";
        public override string ServerVersion => "16.0";
        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

        public override void Close() => throw new NotSupportedException();

        public override void Open() => throw new NotSupportedException();

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            new RecordingTransaction(this, isolationLevel);

        protected override DbCommand CreateDbCommand() => new RecordingCommand(this);
    }

    private sealed class RecordingTransaction(DbConnection connection, IsolationLevel isolationLevel) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => isolationLevel;
        protected override DbConnection DbConnection => connection;

        public override void Commit() => throw new NotSupportedException();

        public override void Rollback() => throw new NotSupportedException();
    }

    private sealed class RecordingCommand(RecordingConnection connection) : DbCommand
    {
        private readonly NpgsqlCommand _parameters = new();

        [AllowNull]
        public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = connection;
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => _parameters.Parameters;

        public override void Cancel() => throw new NotSupportedException();

        public override int ExecuteNonQuery() => throw new NotSupportedException();

        public override object? ExecuteScalar() => Execute(CancellationToken.None);

        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Execute(cancellationToken));

        private object? Execute(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            connection.Commands.Add(
                new CommandRecord(
                    CommandText,
                    CommandTimeout,
                    Transaction,
                    Parameters.Cast<DbParameter>().Select(parameter => parameter.Value).ToArray(),
                    token
                )
            );
            return connection.Results.Dequeue()();
        }

        public override void Prepare() => throw new NotSupportedException();

        protected override DbParameter CreateDbParameter() => new NpgsqlParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _parameters.Dispose();
            base.Dispose(disposing);
        }
    }
}
