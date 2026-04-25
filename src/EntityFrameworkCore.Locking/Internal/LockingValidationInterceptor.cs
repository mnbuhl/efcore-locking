using System.Data.Common;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

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
        InterceptionResult<DbDataReader> result
    )
    {
        ValidateAndPrepare(command, eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        ValidateAndPrepare(command, eventData);
        return new ValueTask<InterceptionResult<DbDataReader>>(result);
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result
    )
    {
        // Use the SQL tag as the reliable signal: AsyncLocal changes in child continuations
        // do not propagate back to the caller's execution context, so LockContext.Current
        // may still be set from a prior locking query when SaveChanges runs its own reader.
        var wasLocking = command.CommandText.Contains(LockTagConstants.Prefix, StringComparison.Ordinal);
        LockContext.Current = null;
        return wasLocking ? WrapReader(result, eventData.Context) : result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default
    )
    {
        var wasLocking = command.CommandText.Contains(LockTagConstants.Prefix, StringComparison.Ordinal);
        LockContext.Current = null;
        return wasLocking
            ? new ValueTask<DbDataReader>(WrapReader(result, eventData.Context))
            : new ValueTask<DbDataReader>(result);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        LockContext.Current = null;
        TranslateAndRethrow(eventData.Exception, eventData.Context);
    }

    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        LockContext.Current = null;
        TranslateAndRethrow(eventData.Exception, eventData.Context);
        return Task.CompletedTask;
    }

    private static void ValidateAndPrepare(DbCommand command, CommandEventData eventData)
    {
        if (!TryExtractLockOptions(command.CommandText, out var lockOptions))
            return;

        if (eventData.Context?.Database.CurrentTransaction is null)
            throw new LockingConfigurationException(
                "ForUpdate/ForShare requires an active transaction. "
                    + "Call BeginTransaction() before executing a locking query."
            );

        var provider = ((IInfrastructure<IServiceProvider>)eventData.Context).Instance.GetService<ILockingProvider>();
        if (provider is null)
            return;

        var preSql = provider.RowLockGenerator.GeneratePreStatementSql(lockOptions!);
        if (preSql is not null && !command.CommandText.StartsWith(preSql, StringComparison.Ordinal))
            command.CommandText = preSql + ";\n" + command.CommandText;
    }

    private static bool TryExtractLockOptions(string commandText, out LockOptions? lockOptions)
    {
        lockOptions = null;
        var prefixIndex = commandText.IndexOf(LockTagConstants.Prefix, StringComparison.Ordinal);
        if (prefixIndex < 0)
            return false;

        var tagEnd = commandText.IndexOf('\n', prefixIndex);
        var tag = tagEnd < 0
            ? commandText[prefixIndex..].TrimEnd()
            : commandText[prefixIndex..tagEnd].TrimEnd();

        return LockTagConstants.TryParse(tag, out lockOptions);
    }

    private static DbDataReader WrapReader(DbDataReader reader, DbContext? context)
    {
        if (context is null)
            return reader;
        var translator = ((IInfrastructure<IServiceProvider>)context).Instance.GetService<IExceptionTranslator>();
        return translator is not null ? new TranslatingDbDataReader(reader, translator) : reader;
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
