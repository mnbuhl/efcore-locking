using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;

namespace EntityFrameworkCore.Locking.MySql;

public sealed class MySqlLockSqlGenerator : ILockSqlGenerator
{
    public string GenerateLockClause(LockOptions options)
    {
        var mode = options.Mode switch
        {
            LockMode.ForUpdate => "FOR UPDATE",
            LockMode.ForShare => "FOR SHARE",
            _ => throw new LockingConfigurationException($"Unsupported lock mode: {options.Mode}"),
        };

        var modifier = options.Behavior switch
        {
            LockBehavior.Wait => string.Empty,
            LockBehavior.SkipLocked => " SKIP LOCKED",
            LockBehavior.NoWait => " NOWAIT",
            _ => throw new LockingConfigurationException($"Unsupported lock behavior: {options.Behavior}"),
        };

        return $"{mode}{modifier}";
    }

    public bool SupportsLockOptions(LockOptions options) => true;

    public string? GeneratePreStatementSql(LockOptions options)
    {
        // MySQL timeout is in seconds (integer, min 1); convert ms ceiling
        if (options.Behavior == LockBehavior.Wait && options.Timeout.HasValue)
        {
            var seconds = Math.Max(1L, (long)Math.Ceiling(options.Timeout.Value.TotalSeconds));
            return $"SET SESSION innodb_lock_wait_timeout = {seconds}";
        }
        return null;
    }
}
