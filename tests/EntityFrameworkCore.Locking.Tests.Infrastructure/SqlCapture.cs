using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

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
        DbDataReader result)
    {
        _commands.Add(command.CommandText);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        _commands.Add(command.CommandText);
        return new ValueTask<DbDataReader>(result);
    }

    public void Clear() => _commands.Clear();
}
