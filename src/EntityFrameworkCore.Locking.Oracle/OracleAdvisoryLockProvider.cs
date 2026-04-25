using System.Data;
using System.Data.Common;
using System.IO.Hashing;
using System.Text;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Internal;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Locking.Oracle;

/// <summary>
/// Oracle advisory lock provider using DBMS_LOCK.REQUEST / DBMS_LOCK.RELEASE with a numeric
/// user lock id. Session-scoped (release_on_commit = FALSE) — the lock survives transactions
/// and is released on explicit RELEASE or session end.
///
/// Why not DBMS_LOCK.ALLOCATE_UNIQUE? That procedure performs an implicit commit (it may
/// insert into DBMS_LOCK_ALLOCATED), which breaks transaction neutrality: any pending DML on
/// the caller's EF transaction would be committed prematurely and a later rollback would not
/// undo those writes. We therefore use the integer-id overload of REQUEST/RELEASE, which does
/// not commit. User lock ids are reserved for application use in the range [0, 1073741823];
/// ids ≥ 1073741824 are reserved for Oracle. We hash the string key via XxHash32 into the
/// user range with a namespace prefix to minimise cross-library collisions.
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
/// Timeout granularity: DBMS_LOCK.REQUEST takes integer seconds; sub-second timeouts round up to 1 second.
/// </summary>
internal sealed class OracleAdvisoryLockProvider : IAdvisoryLockProvider
{
    // DBMS_LOCK.X_MODE = 6 (exclusive).
    private const int ExclusiveMode = 6;

    // MAXWAIT sentinel (32767 seconds) — effectively "wait indefinitely" for DBMS_LOCK.REQUEST.
    private const int MaxWait = 32767;

    // User lock id range is [0, 1073741823] = [0, 2^30 - 1]. Hash output is masked into this range.
    private const int UserLockIdMax = 0x3FFFFFFF;

    // Namespace prefix "EFLK" (0x45464C4B) mixed into the hash input so our ids are unlikely to
    // collide with other libraries using DBMS_LOCK.REQUEST against the same instance.
    private const string NamespacePrefix = "EFLK:";

    private static int ComputeLockId(string key)
    {
        var bytes = Encoding.UTF8.GetBytes(NamespacePrefix + key);
        var hash = XxHash32.HashToUInt32(bytes);
        return (int)(hash & UserLockIdMax);
    }

    // Parameter-only PL/SQL — no ALLOCATE_UNIQUE, no implicit commit. Uses the integer-id
    // overload of DBMS_LOCK.REQUEST/RELEASE.
    private const string AcquirePlSql =
        "DECLARE rc INTEGER;\n"
        + "BEGIN\n"
        + "  rc := DBMS_LOCK.REQUEST(id => :id, lockmode => :mode, timeout => :timeout, release_on_commit => FALSE);\n"
        + "  :rc := rc;\n"
        + "END;";

    private const string ReleasePlSql =
        "DECLARE rc INTEGER;\n" + "BEGIN\n" + "  rc := DBMS_LOCK.RELEASE(id => :id);\n" + "  :rc := rc;\n" + "END;";

    public async Task<IDistributedLockHandle> AcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        TimeSpan? timeout,
        CancellationToken ct
    )
    {
        var lockId = ComputeLockId(key);
        var timeoutSeconds = ToTimeoutSeconds(timeout);

        await using var cmd = BuildAcquireCommand(connection, lockId, timeoutSeconds);
        await using var reg = ct.Register(static state => ((DbCommand)state!).Cancel(), cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        var rc = GetReturnCode(cmd);
        MapReturnCode(rc, key, ct);
        return BuildHandle(context, connection, key, lockId);
    }

    public async Task<IDistributedLockHandle?> TryAcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        CancellationToken ct
    )
    {
        var lockId = ComputeLockId(key);

        // DBMS_LOCK.REQUEST with timeout=0 is the canonical try-acquire form.
        await using var cmd = BuildAcquireCommand(connection, lockId, timeoutSeconds: 0);
        await using var reg = ct.Register(static state => ((DbCommand)state!).Cancel(), cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        var rc = GetReturnCode(cmd);
        if (rc == 1)
            return null; // timeout — lock held by another session
        MapReturnCode(rc, key, ct);
        return BuildHandle(context, connection, key, lockId);
    }

    public IDistributedLockHandle Acquire(DbContext context, DbConnection connection, string key, TimeSpan? timeout)
    {
        var lockId = ComputeLockId(key);
        var timeoutSeconds = ToTimeoutSeconds(timeout);

        using var cmd = BuildAcquireCommand(connection, lockId, timeoutSeconds);
        cmd.ExecuteNonQuery();

        var rc = GetReturnCode(cmd);
        MapReturnCode(rc, key, ct: default);
        return BuildHandle(context, connection, key, lockId);
    }

    public IDistributedLockHandle? TryAcquire(DbContext context, DbConnection connection, string key)
    {
        var lockId = ComputeLockId(key);

        using var cmd = BuildAcquireCommand(connection, lockId, timeoutSeconds: 0);
        cmd.ExecuteNonQuery();

        var rc = GetReturnCode(cmd);
        if (rc == 1)
            return null;
        MapReturnCode(rc, key, ct: default);
        return BuildHandle(context, connection, key, lockId);
    }

    private static DbCommand BuildAcquireCommand(DbConnection connection, int lockId, int timeoutSeconds)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = AcquirePlSql;
        AddParam(cmd, "id", DbType.Int32, lockId);
        AddParam(cmd, "mode", DbType.Int32, ExclusiveMode);
        AddParam(cmd, "timeout", DbType.Int32, timeoutSeconds);
        AddOutParam(cmd, "rc", DbType.Int32);
        return cmd;
    }

    private static DbCommand BuildReleaseCommand(DbConnection connection, int lockId)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = ReleasePlSql;
        AddParam(cmd, "id", DbType.Int32, lockId);
        AddOutParam(cmd, "rc", DbType.Int32);
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
        int lockId
    )
    {
        async Task ReleaseAsync(CancellationToken ct)
        {
            DistributedLockRegistry.Unregister(context, connection, key);
            await using var cmd = BuildReleaseCommand(connection, lockId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        void ReleaseSync()
        {
            DistributedLockRegistry.Unregister(context, connection, key);
            using var cmd = BuildReleaseCommand(connection, lockId);
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

    private static void AddOutParam(DbCommand cmd, string name, DbType type)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.DbType = type;
        p.Direction = ParameterDirection.Output;
        cmd.Parameters.Add(p);
    }
}
