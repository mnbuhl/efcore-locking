using System.Data.Common;
using System.IO.Hashing;
using System.Text;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace EntityFrameworkCore.Locking.PostgreSQL;

internal sealed class PostgresAdvisoryLockProvider : IAdvisoryLockProvider
{
    // Namespace prefix "EFLK" packed into the upper 32 bits of the bigint key.
    private const long NamespaceMask = 0x45464C4B_00000000L;

    public async Task<IDistributedLockHandle> AcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        TimeSpan? timeout,
        CancellationToken ct,
        DistributedLockMode mode
    )
    {
        ValidateMode(mode);
        var lockKey = ComputeKey(key);
        var acquireFunction = GetAcquireFunction(mode);
        try
        {
            var hasExistingTx = context.Database.CurrentTransaction is not null;

            if (timeout.HasValue && !hasExistingTx)
            {
                // Micro-transaction: SET LOCAL is auto-discarded on COMMIT; pg_advisory_lock is session-scoped and survives.
                await using var tx = await ((NpgsqlConnection)connection)
                    .BeginTransactionAsync(ct)
                    .ConfigureAwait(false);
                await using var setCmd = connection.CreateCommand();
                setCmd.Transaction = tx;
                setCmd.CommandText = $"SET LOCAL lock_timeout = '{(long)timeout.Value.TotalMilliseconds}ms'";
                await setCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

                await using var lockCmd = connection.CreateCommand();
                lockCmd.Transaction = tx;
                lockCmd.CommandText = $"SELECT {acquireFunction}($1)";
                lockCmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = lockKey });
                await lockCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);

                await tx.CommitAsync(ct).ConfigureAwait(false);
            }
            else
            {
                await using var lockCmd = connection.CreateCommand();
                if (timeout.HasValue)
                {
                    // Active transaction already open — SET LOCAL scopes to it, which is fine.
                    await using var setCmd = connection.CreateCommand();
                    setCmd.CommandText = $"SET LOCAL lock_timeout = '{(long)timeout.Value.TotalMilliseconds}ms'";
                    await setCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
                lockCmd.CommandText = $"SELECT {acquireFunction}($1)";
                lockCmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = lockKey });
                await lockCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PostgresException ex) when (ex.SqlState is "55P03" or "57014")
        {
            throw new LockTimeoutException($"Timed out waiting for distributed lock '{key}'.", ex);
        }

        return BuildHandle(context, connection, key, lockKey, mode);
    }

    public async Task<IDistributedLockHandle?> TryAcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        CancellationToken ct,
        DistributedLockMode mode
    )
    {
        ValidateMode(mode);
        var lockKey = ComputeKey(key);
        var tryAcquireFunction = GetTryAcquireFunction(mode);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {tryAcquireFunction}($1)";
        cmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = lockKey });
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is false or null)
            return null;
        return BuildHandle(context, connection, key, lockKey, mode);
    }

    public IDistributedLockHandle Acquire(
        DbContext context,
        DbConnection connection,
        string key,
        TimeSpan? timeout,
        DistributedLockMode mode
    )
    {
        ValidateMode(mode);
        var lockKey = ComputeKey(key);
        var acquireFunction = GetAcquireFunction(mode);
        try
        {
            var hasExistingTx = context.Database.CurrentTransaction is not null;

            if (timeout.HasValue && !hasExistingTx)
            {
                using var tx = ((NpgsqlConnection)connection).BeginTransaction();
                using var setCmd = connection.CreateCommand();
                setCmd.Transaction = tx;
                setCmd.CommandText = $"SET LOCAL lock_timeout = '{(long)timeout.Value.TotalMilliseconds}ms'";
                setCmd.ExecuteNonQuery();

                using var lockCmd = connection.CreateCommand();
                lockCmd.Transaction = tx;
                lockCmd.CommandText = $"SELECT {acquireFunction}($1)";
                lockCmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = lockKey });
                lockCmd.ExecuteScalar();

                tx.Commit();
            }
            else
            {
                if (timeout.HasValue)
                {
                    using var setCmd = connection.CreateCommand();
                    setCmd.CommandText = $"SET LOCAL lock_timeout = '{(long)timeout.Value.TotalMilliseconds}ms'";
                    setCmd.ExecuteNonQuery();
                }
                using var lockCmd = connection.CreateCommand();
                lockCmd.CommandText = $"SELECT {acquireFunction}($1)";
                lockCmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = lockKey });
                lockCmd.ExecuteScalar();
            }
        }
        catch (PostgresException ex) when (ex.SqlState is "55P03" or "57014")
        {
            throw new LockTimeoutException($"Timed out waiting for distributed lock '{key}'.", ex);
        }

        return BuildHandle(context, connection, key, lockKey, mode);
    }

    public IDistributedLockHandle? TryAcquire(
        DbContext context,
        DbConnection connection,
        string key,
        DistributedLockMode mode
    )
    {
        ValidateMode(mode);
        var lockKey = ComputeKey(key);
        var tryAcquireFunction = GetTryAcquireFunction(mode);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {tryAcquireFunction}($1)";
        cmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = lockKey });
        var result = cmd.ExecuteScalar();
        if (result is false or null)
            return null;
        return BuildHandle(context, connection, key, lockKey, mode);
    }

    public void ValidateMode(DistributedLockMode mode)
    {
        switch (mode)
        {
            case DistributedLockMode.Exclusive:
            case DistributedLockMode.Shared:
                return;
            default:
                throw new LockingConfigurationException($"Unsupported distributed lock mode '{mode}'.");
        }
    }

    private static IDistributedLockHandle BuildHandle(
        DbContext context,
        DbConnection connection,
        string key,
        long lockKey,
        DistributedLockMode mode
    )
    {
        var releaseFunction = GetReleaseFunction(mode);

        async Task ReleaseAsync(CancellationToken ct)
        {
            DistributedLockRegistry.Unregister(context, connection, key);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT {releaseFunction}($1)";
            cmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = lockKey });
            await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        }

        void ReleaseSync()
        {
            DistributedLockRegistry.Unregister(context, connection, key);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT {releaseFunction}($1)";
            cmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = lockKey });
            cmd.ExecuteScalar();
        }

        return new DistributedLockHandle(key, connection, openedByConnection: false, ReleaseAsync, ReleaseSync);
    }

    internal static long ComputeKey(string key)
    {
        var bytes = Encoding.UTF8.GetBytes(key);
        var hash = XxHash32.HashToUInt32(bytes);
        return NamespaceMask | hash;
    }

    private static string GetAcquireFunction(DistributedLockMode mode) =>
        mode switch
        {
            DistributedLockMode.Exclusive => "pg_advisory_lock",
            DistributedLockMode.Shared => "pg_advisory_lock_shared",
            _ => throw new LockingConfigurationException($"Unsupported distributed lock mode '{mode}'."),
        };

    private static string GetTryAcquireFunction(DistributedLockMode mode) =>
        mode switch
        {
            DistributedLockMode.Exclusive => "pg_try_advisory_lock",
            DistributedLockMode.Shared => "pg_try_advisory_lock_shared",
            _ => throw new LockingConfigurationException($"Unsupported distributed lock mode '{mode}'."),
        };

    private static string GetReleaseFunction(DistributedLockMode mode) =>
        mode switch
        {
            DistributedLockMode.Exclusive => "pg_advisory_unlock",
            DistributedLockMode.Shared => "pg_advisory_unlock_shared",
            _ => throw new LockingConfigurationException($"Unsupported distributed lock mode '{mode}'."),
        };

    public bool SupportsTransactionLocks => true;

    public async Task<IDistributedLockHandle> AcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        TimeSpan? timeout,
        CancellationToken ct,
        DistributedLockMode mode,
        DistributedLockScope scope
    )
    {
        if (scope == DistributedLockScope.Session)
            return await AcquireAsync(context, connection, key, timeout, ct, mode).ConfigureAwait(false);
        ValidateScope(scope);
        await AcquireTransactionAsync(context, connection, key, timeout, ct, mode).ConfigureAwait(false);
        return new TransactionDistributedLockResult(key, true);
    }

    public async Task<IDistributedLockHandle?> TryAcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        CancellationToken ct,
        DistributedLockMode mode,
        DistributedLockScope scope
    )
    {
        if (scope == DistributedLockScope.Session)
            return await TryAcquireAsync(context, connection, key, ct, mode).ConfigureAwait(false);
        ValidateScope(scope);
        var acquired = await TryAcquireTransactionAsync(context, connection, key, ct, mode).ConfigureAwait(false);
        return new TransactionDistributedLockResult(key, acquired);
    }

    public IDistributedLockHandle Acquire(
        DbContext context,
        DbConnection connection,
        string key,
        TimeSpan? timeout,
        DistributedLockMode mode,
        DistributedLockScope scope
    )
    {
        if (scope == DistributedLockScope.Session)
            return Acquire(context, connection, key, timeout, mode);
        ValidateScope(scope);
        AcquireTransaction(context, connection, key, timeout, mode);
        return new TransactionDistributedLockResult(key, true);
    }

    public IDistributedLockHandle? TryAcquire(
        DbContext context,
        DbConnection connection,
        string key,
        DistributedLockMode mode,
        DistributedLockScope scope
    )
    {
        if (scope == DistributedLockScope.Session)
            return TryAcquire(context, connection, key, mode);
        ValidateScope(scope);
        return new TransactionDistributedLockResult(key, TryAcquireTransaction(context, connection, key, mode));
    }

    private static void ValidateScope(DistributedLockScope scope)
    {
        if (scope != DistributedLockScope.Transaction)
            throw new LockingConfigurationException($"Unsupported distributed lock scope '{scope}'.");
    }

    // After timeout setup, failures require caller rollback, including cancellation before dispatch.
    // No recovery SQL is issued on a failed or uncertain transaction.
    private const int SetupTimeoutSeconds = 30;

    private async Task AcquireTransactionAsync(
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
            if (!await TryAcquireTransactionAsync(context, connection, key, ct, mode).ConfigureAwait(false))
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
                await using var set = CreateAcquisitionTimeoutCommand(context, connection, milliseconds);
                await set.ExecuteScalarAsync(ct).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            await using var acquire = CreateTransactionLockCommand(context, connection, key, mode, tryAcquire: false);
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

    private void AcquireTransaction(
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
            if (!TryAcquireTransaction(context, connection, key, mode))
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
                using var set = CreateAcquisitionTimeoutCommand(context, connection, milliseconds);
                set.ExecuteScalar();
            }

            using var acquire = CreateTransactionLockCommand(context, connection, key, mode, tryAcquire: false);
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

    private async Task<bool> TryAcquireTransactionAsync(
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
            await using var command = CreateTransactionLockCommand(context, connection, key, mode, tryAcquire: true);
            return ReadOutcome(await command.ExecuteScalarAsync(ct).ConfigureAwait(false), key);
        }
        catch (PostgresException ex)
        {
            throw Translate(ex, key, ct);
        }
    }

    private bool TryAcquireTransaction(DbContext context, DbConnection connection, string key, DistributedLockMode mode)
    {
        ValidateMode(mode);
        try
        {
            using var command = CreateTransactionLockCommand(context, connection, key, mode, tryAcquire: true);
            return ReadOutcome(command.ExecuteScalar(), key);
        }
        catch (PostgresException ex)
        {
            throw Translate(ex, key, CancellationToken.None);
        }
    }

    private static DbCommand CreateCommand(DbContext context, DbConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = SetupTimeoutSeconds;
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        return command;
    }

    private static DbCommand CreateAcquisitionTimeoutCommand(
        DbContext context,
        DbConnection connection,
        int milliseconds
    )
    {
        // pg_settings exposes lock_timeout in milliseconds; zero disables the caller's timeout.
        var command = CreateCommand(
            context,
            connection,
            "SELECT set_config('lock_timeout', (CASE WHEN setting::int = 0 THEN $1 ELSE LEAST(setting::int, $1) END)::text || 'ms', true) FROM pg_catalog.pg_settings WHERE name = 'lock_timeout'"
        );
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = milliseconds });
        return command;
    }

    private static DbCommand CreateTimeoutCommand(DbContext context, DbConnection connection, string value)
    {
        var command = CreateCommand(context, connection, "SELECT set_config('lock_timeout', $1, true)");
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = value });
        return command;
    }

    private static DbCommand CreateTransactionLockCommand(
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
        command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = ComputeKey(key) });
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
