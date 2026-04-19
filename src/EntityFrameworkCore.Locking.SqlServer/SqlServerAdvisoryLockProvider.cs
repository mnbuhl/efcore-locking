using System.Data;
using System.Data.Common;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Internal;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Locking.SqlServer;

/// <summary>
/// SQL Server advisory lock provider using sp_getapplock / sp_releaseapplock with @LockOwner='Session'.
/// Return codes: 0/1=success, -1=LockTimeoutException, -2=OperationCanceledException,
/// -3=DeadlockException, other negatives=LockAcquisitionFailedException.
/// Cancellation registers ct.Register(() => cmd.Cancel()); SqlCommand.Cancel sends an attention signal.
/// Depending on timing, cancellation may surface as a SqlException rather than sp_getapplock return -2;
/// both paths are translated to OperationCanceledException when ct.IsCancellationRequested.
/// </summary>
internal sealed class SqlServerAdvisoryLockProvider : IAdvisoryLockProvider
{
    public async Task<IDistributedLockHandle> AcquireAsync(
        DbContext context, DbConnection connection, string key, TimeSpan? timeout, CancellationToken ct)
    {
        var timeoutMs = ToTimeoutMs(timeout);
        await using var cmd = BuildAcquireCommand(connection, key, timeoutMs);
        using var reg = ct.Register(static s => ((DbCommand)s!).Cancel(), cmd);
        try
        {
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (SqlException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        catch (OperationCanceledException) { throw; }

        var returnCode = GetReturnCode(cmd);
        return MapReturnCode(returnCode, key, ct)
               ?? BuildHandle(context, connection, key);
    }

    public async Task<IDistributedLockHandle?> TryAcquireAsync(
        DbContext context, DbConnection connection, string key, CancellationToken ct)
    {
        await using var cmd = BuildAcquireCommand(connection, key, timeoutMs: 0);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        var returnCode = GetReturnCode(cmd);
        if (returnCode == -1)
            return null;
        MapReturnCode(returnCode, key, ct);
        return BuildHandle(context, connection, key);
    }

    public IDistributedLockHandle Acquire(
        DbContext context, DbConnection connection, string key, TimeSpan? timeout)
    {
        var timeoutMs = ToTimeoutMs(timeout);
        using var cmd = BuildAcquireCommand(connection, key, timeoutMs);
        cmd.ExecuteNonQuery();
        var returnCode = GetReturnCode(cmd);
        return MapReturnCode(returnCode, key, ct: default)
               ?? BuildHandle(context, connection, key);
    }

    public IDistributedLockHandle? TryAcquire(
        DbContext context, DbConnection connection, string key)
    {
        using var cmd = BuildAcquireCommand(connection, key, timeoutMs: 0);
        cmd.ExecuteNonQuery();
        var returnCode = GetReturnCode(cmd);
        if (returnCode == -1)
            return null;
        MapReturnCode(returnCode, key, ct: default);
        return BuildHandle(context, connection, key);
    }

    private static DbCommand BuildAcquireCommand(DbConnection connection, string key, int timeoutMs)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandText = "sp_getapplock";

        AddParam(cmd, "@Resource", key);
        AddParam(cmd, "@LockMode", "Exclusive");
        AddParam(cmd, "@LockOwner", "Session");
        AddParam(cmd, "@LockTimeout", timeoutMs);
        AddParam(cmd, "@DbPrincipal", "public");

        var returnParam = cmd.CreateParameter();
        returnParam.ParameterName = "@ReturnValue";
        returnParam.Direction = ParameterDirection.ReturnValue;
        returnParam.DbType = DbType.Int32;
        cmd.Parameters.Add(returnParam);

        return cmd;
    }

    private static int GetReturnCode(DbCommand cmd)
        => (int)(cmd.Parameters["@ReturnValue"].Value ?? -999);

    /// <summary>
    /// Maps sp_getapplock return codes. Returns null on success (0 or 1); throws on all error codes.
    /// </summary>
    private static IDistributedLockHandle? MapReturnCode(int code, string key, CancellationToken ct)
    {
        return code switch
        {
            0 or 1 => null, // success — caller constructs the handle
            -1 => throw new LockTimeoutException($"Timed out waiting for distributed lock '{key}'."),
            -2 => throw new OperationCanceledException(ct),
            -3 => throw new DeadlockException($"Deadlock detected acquiring distributed lock '{key}'."),
            _ => throw new LockAcquisitionFailedException($"sp_getapplock returned {code} for key '{key}'.")
        };
    }

    private static IDistributedLockHandle BuildHandle(
        DbContext context, DbConnection connection, string key)
    {
        async Task ReleaseAsync(CancellationToken ct)
        {
            DistributedLockRegistry.Unregister(context, connection, key);
            await using var cmd = BuildReleaseCommand(connection, key);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        void ReleaseSync()
        {
            DistributedLockRegistry.Unregister(context, connection, key);
            using var cmd = BuildReleaseCommand(connection, key);
            cmd.ExecuteNonQuery();
        }

        return new DistributedLockHandle(key, connection, openedByConnection: false, ReleaseAsync, ReleaseSync);
    }

    private static DbCommand BuildReleaseCommand(DbConnection connection, string key)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandText = "sp_releaseapplock";
        AddParam(cmd, "@Resource", key);
        AddParam(cmd, "@LockOwner", "Session");
        AddParam(cmd, "@DbPrincipal", "public");
        return cmd;
    }

    private static int ToTimeoutMs(TimeSpan? timeout)
    {
        if (timeout is null)
            return -1;
        var ms = (long)timeout.Value.TotalMilliseconds;
        return (int)Math.Min(ms, int.MaxValue);
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
