using System.Data.Common;
using System.Globalization;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace EntityFrameworkCore.Locking.PostgreSQL;

/// <remarks>
/// After timeout setup, any failure requires caller rollback, including cancellation before lock dispatch.
/// No recovery SQL is issued on a failed or uncertain transaction.
/// </remarks>
internal sealed class PostgresTransactionAdvisoryLockProvider : ITransactionAdvisoryLockProvider
{
    private const int SetupTimeoutSeconds = 30;

    public async Task AcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        TimeSpan? timeout,
        CancellationToken ct,
        DistributedLockMode mode
    )
    {
        ValidateMode(mode);
        var milliseconds = TransactionDistributedLockTimeout.ToMilliseconds(timeout);
        if (milliseconds == 0)
        {
            if (!await TryAcquireAsync(context, connection, key, ct, mode).ConfigureAwait(false))
                throw new LockTimeoutException($"Timed out waiting for transaction distributed lock '{key}'.");
            return;
        }

        ct.ThrowIfCancellationRequested();
        try
        {
            string? previous = null;
            if (milliseconds > 0)
            {
                await using var read = CreateCommand(context, connection, "SELECT current_setting('lock_timeout')");
                previous = ReadTimeout(await read.ExecuteScalarAsync(ct).ConfigureAwait(false), key);
                await using var set = CreateTimeoutCommand(
                    context,
                    connection,
                    milliseconds.ToString(CultureInfo.InvariantCulture) + "ms"
                );
                await set.ExecuteScalarAsync(ct).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            await using var acquire = CreateLockCommand(context, connection, key, mode, tryAcquire: false);
            await acquire.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (previous is not null)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(SetupTimeoutSeconds));
                try
                {
                    await using var restore = CreateTimeoutCommand(context, connection, previous);
                    await restore.ExecuteScalarAsync(cleanup.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    throw new LockAcquisitionFailedException(
                        $"Could not restore lock_timeout after acquiring transaction distributed lock '{key}'; roll back the transaction.",
                        ex
                    );
                }
            }
        }
        catch (PostgresException ex)
        {
            throw Translate(ex, key, ct);
        }
    }

    public void Acquire(
        DbContext context,
        DbConnection connection,
        string key,
        TimeSpan? timeout,
        DistributedLockMode mode
    )
    {
        ValidateMode(mode);
        var milliseconds = TransactionDistributedLockTimeout.ToMilliseconds(timeout);
        if (milliseconds == 0)
        {
            if (!TryAcquire(context, connection, key, mode))
                throw new LockTimeoutException($"Timed out waiting for transaction distributed lock '{key}'.");
            return;
        }

        try
        {
            string? previous = null;
            if (milliseconds > 0)
            {
                using var read = CreateCommand(context, connection, "SELECT current_setting('lock_timeout')");
                previous = ReadTimeout(read.ExecuteScalar(), key);
                using var set = CreateTimeoutCommand(
                    context,
                    connection,
                    milliseconds.ToString(CultureInfo.InvariantCulture) + "ms"
                );
                set.ExecuteScalar();
            }

            using var acquire = CreateLockCommand(context, connection, key, mode, tryAcquire: false);
            acquire.ExecuteScalar();
            if (previous is not null)
            {
                try
                {
                    using var restore = CreateTimeoutCommand(context, connection, previous);
                    restore.ExecuteScalar();
                }
                catch (Exception ex)
                {
                    throw new LockAcquisitionFailedException(
                        $"Could not restore lock_timeout after acquiring transaction distributed lock '{key}'; roll back the transaction.",
                        ex
                    );
                }
            }
        }
        catch (PostgresException ex)
        {
            throw Translate(ex, key, CancellationToken.None);
        }
    }

    public async Task<bool> TryAcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        CancellationToken ct,
        DistributedLockMode mode
    )
    {
        ValidateMode(mode);
        ct.ThrowIfCancellationRequested();
        try
        {
            await using var command = CreateLockCommand(context, connection, key, mode, tryAcquire: true);
            return ReadOutcome(await command.ExecuteScalarAsync(ct).ConfigureAwait(false), key);
        }
        catch (PostgresException ex)
        {
            throw Translate(ex, key, ct);
        }
    }

    public bool TryAcquire(DbContext context, DbConnection connection, string key, DistributedLockMode mode)
    {
        ValidateMode(mode);
        try
        {
            using var command = CreateLockCommand(context, connection, key, mode, tryAcquire: true);
            return ReadOutcome(command.ExecuteScalar(), key);
        }
        catch (PostgresException ex)
        {
            throw Translate(ex, key, CancellationToken.None);
        }
    }

    public void ValidateMode(DistributedLockMode mode)
    {
        if (mode is not (DistributedLockMode.Exclusive or DistributedLockMode.Shared))
            throw new LockingConfigurationException($"Unsupported distributed lock mode '{mode}'.");
    }

    private static DbCommand CreateCommand(DbContext context, DbConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = SetupTimeoutSeconds;
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        return command;
    }

    private static DbCommand CreateTimeoutCommand(DbContext context, DbConnection connection, string value)
    {
        var command = CreateCommand(context, connection, "SELECT set_config('lock_timeout', $1, true)");
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = value });
        return command;
    }

    private static DbCommand CreateLockCommand(
        DbContext context,
        DbConnection connection,
        string key,
        DistributedLockMode mode,
        bool tryAcquire
    )
    {
        var function = tryAcquire ? "pg_try_advisory_xact_lock" : "pg_advisory_xact_lock";
        if (mode == DistributedLockMode.Shared)
            function += "_shared";
        var command = CreateCommand(context, connection, $"SELECT {function}($1)");
        command.CommandTimeout = 0;
        command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = PostgresAdvisoryLockProvider.ComputeKey(key) });
        return command;
    }

    private static string ReadTimeout(object? value, string key) =>
        value is string text
            ? text
            : throw new LockAcquisitionFailedException(
                $"Could not read lock_timeout for transaction distributed lock '{key}'."
            );

    private static bool ReadOutcome(object? value, string key) =>
        value is bool outcome
            ? outcome
            : throw new LockAcquisitionFailedException(
                $"Unexpected acquisition result for transaction distributed lock '{key}'."
            );

    private static Exception Translate(PostgresException ex, string key, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return new OperationCanceledException(
                $"Transaction distributed lock '{key}' acquisition was canceled.",
                ex,
                ct
            );
        return ex.SqlState switch
        {
            "55P03" or "57014" => new LockTimeoutException(
                $"Timed out waiting for transaction distributed lock '{key}'.",
                ex
            ),
            "40P01" => new DeadlockException($"Deadlock acquiring transaction distributed lock '{key}'.", ex),
            _ => new LockAcquisitionFailedException($"Could not acquire transaction distributed lock '{key}'.", ex),
        };
    }
}
