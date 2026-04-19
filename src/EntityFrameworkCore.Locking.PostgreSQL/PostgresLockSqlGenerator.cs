using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;

namespace EntityFrameworkCore.Locking.PostgreSQL;

/// <summary>Generates PostgreSQL FOR UPDATE / FOR SHARE SQL fragments.</summary>
public sealed class PostgresLockSqlGenerator : ILockSqlGenerator
{
    public string? GenerateLockClause(LockOptions options)
    {
        var mode = options.Mode switch
        {
            LockMode.ForUpdate => "FOR UPDATE",
            LockMode.ForShare => "FOR SHARE",
            LockMode.ForNoKeyUpdate => "FOR NO KEY UPDATE",
            LockMode.ForKeyShare => "FOR KEY SHARE",
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
        // Postgres WAIT with timeout: SET LOCAL lock_timeout (transaction-scoped, reverts at transaction end)
        if (options.Behavior == LockBehavior.Wait && options.Timeout.HasValue)
            return $"SET LOCAL lock_timeout = '{(long)options.Timeout.Value.TotalMilliseconds}ms'";

        return null;
    }
}
