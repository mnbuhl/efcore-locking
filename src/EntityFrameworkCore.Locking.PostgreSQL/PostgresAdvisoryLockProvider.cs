using System.Data;
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

    public async Task<IDistributedLockHandle> AcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        TimeSpan? timeout,
        CancellationToken ct
    )
    {
        var lockKey = ComputeKey(key);
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
                setCmd.CommandText =
                    $"SET LOCAL lock_timeout = '{(long)timeout.Value.TotalMilliseconds}ms'";
                await setCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

                await using var lockCmd = connection.CreateCommand();
                lockCmd.Transaction = tx;
                lockCmd.CommandText = "SELECT pg_advisory_lock($1)";
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
                    setCmd.CommandText =
                        $"SET LOCAL lock_timeout = '{(long)timeout.Value.TotalMilliseconds}ms'";
                    await setCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
                lockCmd.CommandText = "SELECT pg_advisory_lock($1)";
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

        return BuildHandle(context, connection, key, lockKey);
    }

    public async Task<IDistributedLockHandle?> TryAcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        CancellationToken ct
    )
    {
        var lockKey = ComputeKey(key);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT pg_try_advisory_lock($1)";
        cmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = lockKey });
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is false or null)
            return null;
        return BuildHandle(context, connection, key, lockKey);
    }

    public IDistributedLockHandle Acquire(
        DbContext context,
        DbConnection connection,
        string key,
        TimeSpan? timeout
    )
    {
        var lockKey = ComputeKey(key);
        try
        {
            var hasExistingTx = context.Database.CurrentTransaction is not null;

            if (timeout.HasValue && !hasExistingTx)
            {
                using var tx = ((NpgsqlConnection)connection).BeginTransaction();
                using var setCmd = connection.CreateCommand();
                setCmd.Transaction = tx;
                setCmd.CommandText =
                    $"SET LOCAL lock_timeout = '{(long)timeout.Value.TotalMilliseconds}ms'";
                setCmd.ExecuteNonQuery();

                using var lockCmd = connection.CreateCommand();
                lockCmd.Transaction = tx;
                lockCmd.CommandText = "SELECT pg_advisory_lock($1)";
                lockCmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = lockKey });
                lockCmd.ExecuteScalar();

                tx.Commit();
            }
            else
            {
                if (timeout.HasValue)
                {
                    using var setCmd = connection.CreateCommand();
                    setCmd.CommandText =
                        $"SET LOCAL lock_timeout = '{(long)timeout.Value.TotalMilliseconds}ms'";
                    setCmd.ExecuteNonQuery();
                }
                using var lockCmd = connection.CreateCommand();
                lockCmd.CommandText = "SELECT pg_advisory_lock($1)";
                lockCmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = lockKey });
                lockCmd.ExecuteScalar();
            }
        }
        catch (PostgresException ex) when (ex.SqlState is "55P03" or "57014")
        {
            throw new LockTimeoutException($"Timed out waiting for distributed lock '{key}'.", ex);
        }

        return BuildHandle(context, connection, key, lockKey);
    }

    public IDistributedLockHandle? TryAcquire(
        DbContext context,
        DbConnection connection,
        string key
    )
    {
        var lockKey = ComputeKey(key);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT pg_try_advisory_lock($1)";
        cmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = lockKey });
        var result = cmd.ExecuteScalar();
        if (result is false or null)
            return null;
        return BuildHandle(context, connection, key, lockKey);
    }

    private static IDistributedLockHandle BuildHandle(
        DbContext context,
        DbConnection connection,
        string key,
        long lockKey
    )
    {
        async Task ReleaseAsync(CancellationToken ct)
        {
            DistributedLockRegistry.Unregister(context, connection, key);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT pg_advisory_unlock($1)";
            cmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = lockKey });
            await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        }

        void ReleaseSync()
        {
            DistributedLockRegistry.Unregister(context, connection, key);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT pg_advisory_unlock($1)";
            cmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = lockKey });
            cmd.ExecuteScalar();
        }

        return new DistributedLockHandle(
            key,
            connection,
            openedByConnection: false,
            ReleaseAsync,
            ReleaseSync
        );
    }
}
