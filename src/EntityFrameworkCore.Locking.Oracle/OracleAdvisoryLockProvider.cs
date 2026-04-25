using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Internal;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Locking.Oracle;

/// <summary>
/// Oracle advisory lock provider using DBMS_LOCK.ALLOCATE_UNIQUE + DBMS_LOCK.REQUEST/RELEASE.
/// Session-scoped (release_on_commit = FALSE) — lock survives transactions and is released on
/// explicit RELEASE or session end.
///
/// Return codes from DBMS_LOCK.REQUEST / DBMS_LOCK.RELEASE:
///   0 = success
///   1 = timeout
///   2 = deadlock
///   3 = parameter error
///   4 = already own lock (REQUEST) / don't own lock (RELEASE)
///   5 = illegal lock handle
///
/// Prerequisite: the database user must have EXECUTE privilege on DBMS_LOCK:
///   GRANT EXECUTE ON DBMS_LOCK TO &lt;user&gt;;
/// Without this grant, calls surface as ORA-06550 and are translated to LockingConfigurationException.
///
/// Key handling: DBMS_LOCK.ALLOCATE_UNIQUE limits lock names to 128 bytes. Keys over 64 UTF-8 bytes
/// are hashed to "lock:" + hex(SHA256)[..58] = 64 chars total to stay safely under the limit.
/// The "lock:" prefix is reserved — callers using raw keys should avoid that prefix.
///
/// Timeout granularity: DBMS_LOCK.REQUEST takes integer seconds; sub-second timeouts round up to 1 second.
/// </summary>
internal sealed class OracleAdvisoryLockProvider : IAdvisoryLockProvider
{
    // DBMS_LOCK.X_MODE = 6 (exclusive).
    private const int ExclusiveMode = 6;

    // MAXWAIT sentinel (32767 seconds) — effectively "wait indefinitely" for DBMS_LOCK.REQUEST.
    private const int MaxWait = 32767;

    private static string EncodeKey(string key)
    {
        var bytes = Encoding.UTF8.GetBytes(key);
        if (bytes.Length <= 64)
            return key;
        var hash = SHA256.HashData(bytes);
        return "lock:" + Convert.ToHexString(hash)[..58];
    }

    private const string AcquirePlSql =
        "DECLARE lh VARCHAR2(128); rc INTEGER;\n"
        + "BEGIN\n"
        + "  DBMS_LOCK.ALLOCATE_UNIQUE(lockname => :name, lockhandle => lh);\n"
        + "  rc := DBMS_LOCK.REQUEST(lockhandle => lh, lockmode => :mode, timeout => :timeout, release_on_commit => FALSE);\n"
        + "  :handle := lh;\n"
        + "  :rc := rc;\n"
        + "END;";

    private const string ReleasePlSql =
        "DECLARE lh VARCHAR2(128); rc INTEGER;\n"
        + "BEGIN\n"
        + "  DBMS_LOCK.ALLOCATE_UNIQUE(lockname => :name, lockhandle => lh);\n"
        + "  rc := DBMS_LOCK.RELEASE(lockhandle => lh);\n"
        + "  :rc := rc;\n"
        + "END;";

    public async Task<IDistributedLockHandle> AcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        TimeSpan? timeout,
        CancellationToken ct
    )
    {
        var encodedKey = EncodeKey(key);
        var timeoutSeconds = ToTimeoutSeconds(timeout);

        await using var cmd = BuildAcquireCommand(connection, encodedKey, timeoutSeconds);
        await using var reg = ct.Register(static state => ((DbCommand)state!).Cancel(), cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        var rc = GetReturnCode(cmd);
        MapReturnCode(rc, key, ct);
        return BuildHandle(context, connection, key, encodedKey);
    }

    public async Task<IDistributedLockHandle?> TryAcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        CancellationToken ct
    )
    {
        var encodedKey = EncodeKey(key);

        // DBMS_LOCK.REQUEST with timeout=0 is the canonical try-acquire form.
        await using var cmd = BuildAcquireCommand(connection, encodedKey, timeoutSeconds: 0);
        await using var reg = ct.Register(static state => ((DbCommand)state!).Cancel(), cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        var rc = GetReturnCode(cmd);
        if (rc == 1)
            return null; // timeout — lock held by another session
        MapReturnCode(rc, key, ct);
        return BuildHandle(context, connection, key, encodedKey);
    }

    public IDistributedLockHandle Acquire(DbContext context, DbConnection connection, string key, TimeSpan? timeout)
    {
        var encodedKey = EncodeKey(key);
        var timeoutSeconds = ToTimeoutSeconds(timeout);

        using var cmd = BuildAcquireCommand(connection, encodedKey, timeoutSeconds);
        cmd.ExecuteNonQuery();

        var rc = GetReturnCode(cmd);
        MapReturnCode(rc, key, ct: default);
        return BuildHandle(context, connection, key, encodedKey);
    }

    public IDistributedLockHandle? TryAcquire(DbContext context, DbConnection connection, string key)
    {
        var encodedKey = EncodeKey(key);

        using var cmd = BuildAcquireCommand(connection, encodedKey, timeoutSeconds: 0);
        cmd.ExecuteNonQuery();

        var rc = GetReturnCode(cmd);
        if (rc == 1)
            return null;
        MapReturnCode(rc, key, ct: default);
        return BuildHandle(context, connection, key, encodedKey);
    }

    private static DbCommand BuildAcquireCommand(DbConnection connection, string encodedKey, int timeoutSeconds)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = AcquirePlSql;
        AddParam(cmd, "name", DbType.String, encodedKey);
        AddParam(cmd, "mode", DbType.Int32, ExclusiveMode);
        AddParam(cmd, "timeout", DbType.Int32, timeoutSeconds);
        AddOutParam(cmd, "handle", DbType.String, size: 128);
        AddOutParam(cmd, "rc", DbType.Int32, size: 0);
        return cmd;
    }

    private static DbCommand BuildReleaseCommand(DbConnection connection, string encodedKey)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = ReleasePlSql;
        AddParam(cmd, "name", DbType.String, encodedKey);
        AddOutParam(cmd, "rc", DbType.Int32, size: 0);
        return cmd;
    }

    private static int GetReturnCode(DbCommand cmd)
    {
        var value = cmd.Parameters["rc"].Value;
        if (value is null or DBNull)
            return -1;
        return Convert.ToInt32(value);
    }

    private static void MapReturnCode(int code, string key, CancellationToken ct)
    {
        switch (code)
        {
            case 0:
            case 4: // already own lock — treat as success (idempotent acquire)
                return;
            case 1:
                throw new LockTimeoutException($"Timed out waiting for distributed lock '{key}'.");
            case 2:
                throw new DeadlockException($"Deadlock detected acquiring distributed lock '{key}'.");
            case 3:
                throw new LockingConfigurationException(
                    $"DBMS_LOCK.REQUEST returned parameter error (code 3) for key '{key}'."
                );
            case 5:
                throw new LockingConfigurationException(
                    $"DBMS_LOCK.REQUEST returned illegal lock handle (code 5) for key '{key}'."
                );
            default:
                if (ct.IsCancellationRequested)
                    throw new OperationCanceledException(ct);
                throw new LockAcquisitionFailedException(
                    $"DBMS_LOCK.REQUEST returned unexpected code {code} for key '{key}'."
                );
        }
    }

    private static IDistributedLockHandle BuildHandle(
        DbContext context,
        DbConnection connection,
        string key,
        string encodedKey
    )
    {
        async Task ReleaseAsync(CancellationToken ct)
        {
            DistributedLockRegistry.Unregister(context, connection, key);
            await using var cmd = BuildReleaseCommand(connection, encodedKey);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        void ReleaseSync()
        {
            DistributedLockRegistry.Unregister(context, connection, key);
            using var cmd = BuildReleaseCommand(connection, encodedKey);
            cmd.ExecuteNonQuery();
        }

        return new DistributedLockHandle(key, connection, openedByConnection: false, ReleaseAsync, ReleaseSync);
    }

    private static int ToTimeoutSeconds(TimeSpan? timeout)
    {
        if (timeout is null)
            return MaxWait;
        var seconds = (long)Math.Ceiling(timeout.Value.TotalSeconds);
        if (seconds < 1)
            seconds = 1;
        if (seconds > MaxWait)
            seconds = MaxWait;
        return (int)seconds;
    }

    private static void AddParam(DbCommand cmd, string name, DbType type, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.DbType = type;
        p.Direction = ParameterDirection.Input;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static void AddOutParam(DbCommand cmd, string name, DbType type, int size)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.DbType = type;
        p.Direction = ParameterDirection.Output;
        if (size > 0)
            p.Size = size;
        cmd.Parameters.Add(p);
    }
}
