using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;

namespace EntityFrameworkCore.Locking.Internal;

/// <summary>
/// Validates transaction presence, emits pre-statement SQL (e.g. SET LOCAL lock_timeout),
/// translates DB exceptions to typed locking exceptions, and clears LockContext on completion.
/// Does NOT rewrite SQL — validation and cleanup only.
/// Stateless: resolves ILockingProvider from the DbContext service provider per-command.
/// </summary>
public sealed class LockingValidationInterceptor : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        ValidateAndPrepare(command, eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        ValidateAndPrepare(command, eventData);
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
        TranslateAndRethrow(eventData.Exception, eventData.Context);
    }

    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        LockContext.Current = null;
        TranslateAndRethrow(eventData.Exception, eventData.Context);
        return Task.CompletedTask;
    }

    private static void ValidateAndPrepare(DbCommand command, CommandEventData eventData)
    {
        var lockOptions = LockContext.Current;
        if (lockOptions is null)
            return;

        if (eventData.Context?.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "ForUpdate requires an active transaction. " +
                "Call BeginTransaction() before executing a locking query.");

        var provider = ((IInfrastructure<IServiceProvider>)eventData.Context).Instance.GetService<ILockingProvider>();
        if (provider is null)
            return;

        var preSql = provider.RowLockGenerator.GeneratePreStatementSql(lockOptions);
        if (preSql is not null && !command.CommandText.StartsWith(preSql, StringComparison.Ordinal))
            command.CommandText = preSql + ";\n" + command.CommandText;
    }

    private static void TranslateAndRethrow(Exception? exception, DbContext? context)
    {
        if (exception is null || context is null)
            return;

        var translator = ((IInfrastructure<IServiceProvider>)context).Instance.GetService<IExceptionTranslator>();
        if (translator is null)
            return;

        var translated = translator.Translate(exception);
        if (translated is not null)
            throw translated;
    }
}
