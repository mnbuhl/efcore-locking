using System.Data;
using System.Data.Common;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Internal;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Locking.SqlServer;

/// <summary>
/// SQL Server advisory lock provider using native session-owned or transaction-owned application locks.
/// Return codes: 0/1=success, -1=LockTimeoutException, -2=OperationCanceledException,
/// -3=DeadlockException, other negatives=LockAcquisitionFailedException.
/// Cancellation registers ct.Register(() => cmd.Cancel()); SqlCommand.Cancel sends an attention signal.
/// Depending on timing, cancellation may surface as a SqlException rather than sp_getapplock return -2;
/// both paths are translated to OperationCanceledException when ct.IsCancellationRequested.
/// </summary>
internal sealed class SqlServerAdvisoryLockProvider : IAdvisoryLockProvider
{
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
        var timeoutMs = ToTimeoutMs(timeout);
        await using var cmd = BuildAcquireCommand(connection, key, timeoutMs, mode);
        await using var reg = ct.Register(static s => ((DbCommand)s!).Cancel(), cmd);
        try
        {
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (SqlException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }

        var returnCode = GetReturnCode(cmd);
        return MapReturnCode(returnCode, key, ct) ?? BuildHandle(context, connection, key);
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
        await using var cmd = BuildAcquireCommand(connection, key, timeoutMs: 0, mode);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        var returnCode = GetReturnCode(cmd);
        if (returnCode == -1)
            return null;
        MapReturnCode(returnCode, key, ct);
        return BuildHandle(context, connection, key);
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
        var timeoutMs = ToTimeoutMs(timeout);
        using var cmd = BuildAcquireCommand(connection, key, timeoutMs, mode);
        cmd.ExecuteNonQuery();
        var returnCode = GetReturnCode(cmd);
        return MapReturnCode(returnCode, key, ct: default) ?? BuildHandle(context, connection, key);
    }

    public IDistributedLockHandle? TryAcquire(
        DbContext context,
        DbConnection connection,
        string key,
        DistributedLockMode mode
    )
    {
        ValidateMode(mode);
        using var cmd = BuildAcquireCommand(connection, key, timeoutMs: 0, mode);
        cmd.ExecuteNonQuery();
        var returnCode = GetReturnCode(cmd);
        if (returnCode == -1)
            return null;
        MapReturnCode(returnCode, key, ct: default);
        return BuildHandle(context, connection, key);
    }

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
        ValidateScope(scope);
        if (scope == DistributedLockScope.Session)
            return await AcquireAsync(context, connection, key, timeout, ct, mode).ConfigureAwait(false);

        await ExecuteTransactionAsync(
                context,
                connection,
                key,
                TransactionDistributedLockTimeout.ToMilliseconds(timeout),
                ct,
                mode,
                tryAcquire: false
            )
            .ConfigureAwait(false);
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
        ValidateScope(scope);
        if (scope == DistributedLockScope.Session)
            return await TryAcquireAsync(context, connection, key, ct, mode).ConfigureAwait(false);

        var acquired = await ExecuteTransactionAsync(context, connection, key, 0, ct, mode, tryAcquire: true)
            .ConfigureAwait(false);
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
        ValidateScope(scope);
        if (scope == DistributedLockScope.Session)
            return Acquire(context, connection, key, timeout, mode);

        ExecuteTransaction(
            context,
            connection,
            key,
            TransactionDistributedLockTimeout.ToMilliseconds(timeout),
            mode,
            tryAcquire: false
        );
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
        ValidateScope(scope);
        if (scope == DistributedLockScope.Session)
            return TryAcquire(context, connection, key, mode);

        var acquired = ExecuteTransaction(context, connection, key, 0, mode, tryAcquire: true);
        return new TransactionDistributedLockResult(key, acquired);
    }

    private static void ValidateScope(DistributedLockScope scope)
    {
        if (scope is not DistributedLockScope.Session and not DistributedLockScope.Transaction)
            throw new LockingConfigurationException($"Unsupported distributed lock scope '{scope}'.");
    }

    private static DbCommand BuildAcquireCommand(
        DbConnection connection,
        string key,
        int timeoutMs,
        DistributedLockMode mode,
        DbContext? transactionContext = null
    )
    {
        var sqlMode = ToSqlServerLockMode(mode);
        var cmd = connection.CreateCommand();
        if (transactionContext is not null)
        {
            // The native lock budget, not the driver's default 30-second budget, governs acquisition.
            cmd.CommandTimeout = 0;
            cmd.Transaction = transactionContext.Database.CurrentTransaction?.GetDbTransaction();
        }
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandText = "sp_getapplock";

        AddParam(cmd, "@Resource", key);
        AddParam(cmd, "@LockMode", sqlMode);
        AddParam(cmd, "@LockOwner", transactionContext is null ? "Session" : "Transaction");
        AddParam(cmd, "@LockTimeout", timeoutMs);
        AddParam(cmd, "@DbPrincipal", "public");

        var returnParam = cmd.CreateParameter();
        returnParam.ParameterName = "@ReturnValue";
        returnParam.Direction = ParameterDirection.ReturnValue;
        returnParam.DbType = DbType.Int32;
        cmd.Parameters.Add(returnParam);

        return cmd;
    }

    private static async Task<bool> ExecuteTransactionAsync(
        DbContext context,
        DbConnection connection,
        string key,
        int timeoutMs,
        CancellationToken ct,
        DistributedLockMode mode,
        bool tryAcquire
    )
    {
        ct.ThrowIfCancellationRequested();
        await using var command = BuildAcquireCommand(connection, key, timeoutMs, mode, context);
        using var registration = ct.Register(static state => Cancel((DbCommand)state!), command);
        try
        {
            ct.ThrowIfCancellationRequested();
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (SqlException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            throw new DeadlockException($"Deadlock detected acquiring distributed lock '{key}'.", exception);
        }

        return MapTransactionReturnCode(command, key, ct, tryAcquire);
    }

    private static bool ExecuteTransaction(
        DbContext context,
        DbConnection connection,
        string key,
        int timeoutMs,
        DistributedLockMode mode,
        bool tryAcquire
    )
    {
        using var command = BuildAcquireCommand(connection, key, timeoutMs, mode, context);
        try
        {
            command.ExecuteNonQuery();
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            throw new DeadlockException($"Deadlock detected acquiring distributed lock '{key}'.", exception);
        }

        return MapTransactionReturnCode(command, key, default, tryAcquire);
    }

    private static void Cancel(DbCommand command)
    {
        // Cancellation is best effort: driver cancellation errors must not escape the token callback.
        try
        {
            command.Cancel();
        }
        catch (Exception) { }
    }

    private static bool MapTransactionReturnCode(DbCommand command, string key, CancellationToken ct, bool tryAcquire)
    {
        var code = command.Parameters["@ReturnValue"].Value as int?;
        if (code is null)
            throw new LockAcquisitionFailedException($"sp_getapplock returned no return code for key '{key}'.");
        if (code == -1 && tryAcquire)
            return false;
        MapReturnCode(code.Value, key, ct);
        return true;
    }

    private static int GetReturnCode(DbCommand cmd) => (int)(cmd.Parameters["@ReturnValue"].Value ?? -999);

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
            _ => throw new LockAcquisitionFailedException($"sp_getapplock returned {code} for key '{key}'."),
        };
    }

    private static IDistributedLockHandle BuildHandle(DbContext context, DbConnection connection, string key)
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

    public void ValidateMode(DistributedLockMode mode) => _ = ToSqlServerLockMode(mode);

    private static string ToSqlServerLockMode(DistributedLockMode mode) =>
        mode switch
        {
            DistributedLockMode.Exclusive => "Exclusive",
            DistributedLockMode.Shared => "Shared",
            _ => throw new LockingConfigurationException($"Unsupported distributed lock mode '{mode}'."),
        };

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
