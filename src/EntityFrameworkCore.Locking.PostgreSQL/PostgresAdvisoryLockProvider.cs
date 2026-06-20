using System.Data.Common;
using System.IO.Hashing;
using System.Text;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Internal;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EntityFrameworkCore.Locking.PostgreSQL;

internal sealed class PostgresAdvisoryLockProvider : IAdvisoryLockProvider
{
    // Namespace prefix "EFLK" packed into the upper 32 bits of the bigint key.
    private const long NamespaceMask = 0x45464C4B_00000000L;

    private static long ComputeKey(string key)
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
}
