using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Internal;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Locking.MySql;

/// <summary>
/// MySQL advisory lock provider using GET_LOCK / RELEASE_LOCK.
/// Keys longer than 64 chars are deterministically hashed to "lock:" + hex(SHA256)[..58] = 64 chars total.
/// Note: the "lock:" prefix is reserved — callers using raw keys should avoid that prefix.
/// Cancellation sends a KILL QUERY to the server via MySqlConnector's side-channel;
/// if the server does not honor it, cancellation may be delayed by up to the timeout.
/// Sub-second timeouts are rounded up to the next whole second (MySQL granularity).
/// </summary>
internal sealed class MySqlAdvisoryLockProvider : IAdvisoryLockProvider
{
    private static string EncodeKey(string key)
    {
        var bytes = Encoding.UTF8.GetBytes(key);
        if (bytes.Length <= 64)
            return key;
        var hash = SHA256.HashData(bytes);
        return "lock:" + Convert.ToHexString(hash)[..58];
    }

    public async Task<IDistributedLockHandle> AcquireAsync(
        DbContext context, DbConnection connection, string key, TimeSpan? timeout, CancellationToken ct)
    {
        var encodedKey = EncodeKey(key);
        var timeoutSeconds = timeout.HasValue ? (long)Math.Ceiling(timeout.Value.TotalSeconds) : -1L;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT GET_LOCK(@key, @timeout)";
        AddParam(cmd, "@key", encodedKey);
        AddParam(cmd, "@timeout", timeoutSeconds);

        using var reg = ct.Register(static state => ((DbCommand)state!).Cancel(), cmd);
        object? result;
        try
        {
            result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }

        return result switch
        {
            1L or 1 => BuildHandle(context, connection, key, encodedKey),
            0L or 0 => throw new LockTimeoutException($"Timed out waiting for distributed lock '{key}'."),
            _ => throw new LockAcquisitionFailedException($"GET_LOCK returned NULL for key '{key}' — possible error on the server.")
        };
    }

    public async Task<IDistributedLockHandle?> TryAcquireAsync(
        DbContext context, DbConnection connection, string key, CancellationToken ct)
    {
        var encodedKey = EncodeKey(key);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT GET_LOCK(@key, 0)";
        AddParam(cmd, "@key", encodedKey);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is DBNull or null)
            throw new LockAcquisitionFailedException($"GET_LOCK returned NULL for key '{key}' — possible error on the server.");
        return result is 1L or 1
            ? BuildHandle(context, connection, key, encodedKey)
            : null;
    }

    public IDistributedLockHandle Acquire(
        DbContext context, DbConnection connection, string key, TimeSpan? timeout)
    {
        var encodedKey = EncodeKey(key);
        var timeoutSeconds = timeout.HasValue ? (long)Math.Ceiling(timeout.Value.TotalSeconds) : -1L;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT GET_LOCK(@key, @timeout)";
        AddParam(cmd, "@key", encodedKey);
        AddParam(cmd, "@timeout", timeoutSeconds);

        var result = cmd.ExecuteScalar();
        return result switch
        {
            1L or 1 => BuildHandle(context, connection, key, encodedKey),
            0L or 0 => throw new LockTimeoutException($"Timed out waiting for distributed lock '{key}'."),
            _ => throw new LockAcquisitionFailedException($"GET_LOCK returned NULL for key '{key}' — possible error on the server.")
        };
    }

    public IDistributedLockHandle? TryAcquire(
        DbContext context, DbConnection connection, string key)
    {
        var encodedKey = EncodeKey(key);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT GET_LOCK(@key, 0)";
        AddParam(cmd, "@key", encodedKey);

        var result = cmd.ExecuteScalar();
        if (result is DBNull or null)
            throw new LockAcquisitionFailedException($"GET_LOCK returned NULL for key '{key}' — possible error on the server.");
        return result is 1L or 1
            ? BuildHandle(context, connection, key, encodedKey)
            : null;
    }

    private static IDistributedLockHandle BuildHandle(
        DbContext context, DbConnection connection, string key, string encodedKey)
    {
        async Task ReleaseAsync(CancellationToken ct)
        {
            DistributedLockRegistry.Unregister(context, connection, key);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT RELEASE_LOCK(@key)";
            AddParam(cmd, "@key", encodedKey);
            await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        }

        void ReleaseSync()
        {
            DistributedLockRegistry.Unregister(context, connection, key);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT RELEASE_LOCK(@key)";
            AddParam(cmd, "@key", encodedKey);
            cmd.ExecuteScalar();
        }

        return new DistributedLockHandle(key, connection, openedByConnection: false, ReleaseAsync, ReleaseSync);
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
