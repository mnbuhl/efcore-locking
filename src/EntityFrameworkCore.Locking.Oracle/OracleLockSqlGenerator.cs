using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;

namespace EntityFrameworkCore.Locking.Oracle;

/// <summary>
/// Generates Oracle FOR UPDATE SQL fragments.
/// Oracle supports FOR UPDATE, FOR UPDATE NOWAIT, FOR UPDATE SKIP LOCKED, and FOR UPDATE WAIT {n}.
/// Oracle does NOT support a row-level shared lock (FOR SHARE) — only table-level LOCK TABLE IN SHARE MODE.
/// Timeout granularity for WAIT is whole seconds only; sub-second timeouts are rounded up to 1 second.
/// </summary>
public sealed class OracleLockSqlGenerator : ILockSqlGenerator
{
    public string GenerateLockClause(LockOptions options)
    {
        if (options.Mode != LockMode.ForUpdate)
            throw new LockingConfigurationException(
                $"Oracle does not support lock mode {options.Mode}. Only ForUpdate is supported "
                    + "(Oracle has no row-level shared lock; use ForUpdate)."
            );

        return options.Behavior switch
        {
            LockBehavior.Wait when options.Timeout.HasValue => $"FOR UPDATE WAIT {WaitSeconds(options.Timeout.Value)}",
            LockBehavior.Wait => "FOR UPDATE",
            LockBehavior.SkipLocked => "FOR UPDATE SKIP LOCKED",
            LockBehavior.NoWait => "FOR UPDATE NOWAIT",
            _ => throw new LockingConfigurationException($"Unsupported lock behavior: {options.Behavior}"),
        };
    }

    public bool SupportsLockOptions(LockOptions options) => options.Mode == LockMode.ForUpdate;

    public string? GeneratePreStatementSql(LockOptions options) => null;

    private static long WaitSeconds(TimeSpan timeout) => Math.Max(1L, (long)Math.Ceiling(timeout.TotalSeconds));
}
