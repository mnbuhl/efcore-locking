using System.Data;
using System.Data.Common;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Internal;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Locking.SqlServer;

/// <summary>Uses native application locks owned and released by the caller's transaction.</summary>
internal sealed class SqlServerTransactionAdvisoryLockProvider : ITransactionAdvisoryLockProvider
{
    public async Task AcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        TimeSpan? timeout,
        CancellationToken ct,
        DistributedLockMode mode
    )
    {
        await ExecuteAsync(
                context,
                connection,
                key,
                TransactionDistributedLockTimeout.ToMilliseconds(timeout),
                ct,
                mode,
                tryAcquire: false
            )
            .ConfigureAwait(false);
    }

    public Task<bool> TryAcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        CancellationToken ct,
        DistributedLockMode mode
    ) => ExecuteAsync(context, connection, key, 0, ct, mode, tryAcquire: true);

    public void Acquire(
        DbContext context,
        DbConnection connection,
        string key,
        TimeSpan? timeout,
        DistributedLockMode mode
    ) =>
        Execute(
            context,
            connection,
            key,
            TransactionDistributedLockTimeout.ToMilliseconds(timeout),
            mode,
            tryAcquire: false
        );

    public bool TryAcquire(DbContext context, DbConnection connection, string key, DistributedLockMode mode) =>
        Execute(context, connection, key, 0, mode, tryAcquire: true);

    private static async Task<bool> ExecuteAsync(
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
        await using var command = BuildCommand(context, connection, key, timeoutMs, mode);
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

        return MapReturnCode(command, key, ct, tryAcquire);
    }

    private static bool Execute(
        DbContext context,
        DbConnection connection,
        string key,
        int timeoutMs,
        DistributedLockMode mode,
        bool tryAcquire
    )
    {
        using var command = BuildCommand(context, connection, key, timeoutMs, mode);
        try
        {
            command.ExecuteNonQuery();
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            throw new DeadlockException($"Deadlock detected acquiring distributed lock '{key}'.", exception);
        }

        return MapReturnCode(command, key, default, tryAcquire);
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

    private static DbCommand BuildCommand(
        DbContext context,
        DbConnection connection,
        string key,
        int timeoutMs,
        DistributedLockMode mode
    )
    {
        var sqlMode = ToSqlServerLockMode(mode);
        var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "sp_getapplock";
        // The native lock budget, not the driver's default 30-second budget, governs acquisition.
        command.CommandTimeout = 0;
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        AddParameter(command, "@Resource", key);
        AddParameter(command, "@LockMode", sqlMode);
        AddParameter(command, "@LockOwner", "Transaction");
        AddParameter(command, "@LockTimeout", timeoutMs);
        AddParameter(command, "@DbPrincipal", "public");
        var returnParameter = command.CreateParameter();
        returnParameter.ParameterName = "@ReturnValue";
        returnParameter.Direction = ParameterDirection.ReturnValue;
        returnParameter.DbType = DbType.Int32;
        command.Parameters.Add(returnParameter);
        return command;
    }

    private static bool MapReturnCode(DbCommand command, string key, CancellationToken ct, bool tryAcquire)
    {
        var code = command.Parameters["@ReturnValue"].Value as int?;
        return code switch
        {
            0 or 1 => true,
            -1 when tryAcquire => false,
            -1 => throw new LockTimeoutException($"Timed out waiting for distributed lock '{key}'."),
            -2 => throw new OperationCanceledException(ct),
            -3 => throw new DeadlockException($"Deadlock detected acquiring distributed lock '{key}'."),
            _ => throw new LockAcquisitionFailedException(
                $"sp_getapplock returned {code?.ToString() ?? "no return code"} for key '{key}'."
            ),
        };
    }

    public void ValidateMode(DistributedLockMode mode) => _ = ToSqlServerLockMode(mode);

    private static string ToSqlServerLockMode(DistributedLockMode mode) =>
        mode switch
        {
            DistributedLockMode.Exclusive => "Exclusive",
            DistributedLockMode.Shared => "Shared",
            _ => throw new LockingConfigurationException($"Unsupported distributed lock mode '{mode}'."),
        };

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
