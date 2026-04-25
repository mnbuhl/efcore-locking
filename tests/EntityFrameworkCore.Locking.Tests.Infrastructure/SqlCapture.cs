using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EntityFrameworkCore.Locking.Tests.Infrastructure;

/// <summary>
/// Captures the final executed SQL command text (after all interceptor modifications).
/// Register via AddInterceptors() after UseLocking() to capture the fully-modified SQL.
/// </summary>
public sealed class SqlCapture : DbCommandInterceptor
{
    private readonly List<string> _commands = [];

    public IReadOnlyList<string> Commands => _commands;

    public string? LastCommand => _commands.Count > 0 ? _commands[^1] : null;

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result
    )
    {
        _commands.Add(command.CommandText);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default
    )
    {
        _commands.Add(command.CommandText);
        return new ValueTask<DbDataReader>(result);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        _commands.Add(command.CommandText);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default
    )
    {
        _commands.Add(command.CommandText);
        return new ValueTask<object?>(result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        _commands.Add(command.CommandText);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        _commands.Add(command.CommandText);
        return new ValueTask<int>(result);
    }

    public void Clear() => _commands.Clear();
}
