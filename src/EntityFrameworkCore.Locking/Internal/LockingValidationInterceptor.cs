using EntityFrameworkCore.Locking.Exceptions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace EntityFrameworkCore.Locking.Internal;

/// <summary>
/// Validates that a transaction is present when a locking query executes,
/// and clears LockContext after each command (success or failure).
/// Does NOT modify SQL — validation only.
/// </summary>
public sealed class LockingValidationInterceptor : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        ValidateTransaction(eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        ValidateTransaction(eventData);
        return new ValueTask<InterceptionResult<DbDataReader>>(result);
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        LockContext.Current = null;
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        LockContext.Current = null;
        return new ValueTask<DbDataReader>(result);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        LockContext.Current = null;
    }

    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        LockContext.Current = null;
        return Task.CompletedTask;
    }

    private static void ValidateTransaction(CommandEventData eventData)
    {
        if (LockContext.Current is null)
            return;

        if (eventData.Context?.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "ForUpdate requires an active transaction. " +
                "Call BeginTransaction() before executing a locking query.");
    }
}
