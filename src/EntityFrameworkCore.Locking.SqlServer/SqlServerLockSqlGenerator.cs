using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;

namespace EntityFrameworkCore.Locking.SqlServer;

public sealed class SqlServerLockSqlGenerator : ILockSqlGenerator
{
    // SQL Server uses table hints, not trailing clauses — the clause is embedded via VisitTable
    public string? GenerateLockClause(LockOptions options) => BuildTableHint(options);

    public bool SupportsLockOptions(LockOptions options)
    {
        // SQL Server does not support FOR SHARE
        if (options.Mode == LockMode.ForShare)
            return false;
        return true;
    }

    public string? GeneratePreStatementSql(LockOptions options)
    {
        // SET LOCK_TIMEOUT in milliseconds (-1 = infinite, 0 = NOWAIT equivalent)
        if (options.Behavior == LockBehavior.Wait && options.Timeout.HasValue)
            return $"SET LOCK_TIMEOUT {(long)options.Timeout.Value.TotalMilliseconds}";

        if (options.Behavior == LockBehavior.NoWait)
            return "SET LOCK_TIMEOUT 0";

        return null;
    }

    internal static string BuildTableHint(LockOptions options)
    {
        if (options.Behavior == LockBehavior.SkipLocked)
            // READPAST: skip rows locked by other transactions (SQL Server's SKIP LOCKED equivalent).
            // HOLDLOCK is intentionally omitted — it implies SERIALIZABLE range locks which conflict
            // with skip-locked semantics. Limitation: READPAST only skips row/page locks; rows held
            // under a table-level lock are blocked rather than skipped.
            return "WITH (UPDLOCK, ROWLOCK, READPAST)";

        // UPDLOCK: upgrade to update lock; HOLDLOCK: hold until end of tx (= SERIALIZABLE for this row);
        // ROWLOCK: prefer row-level granularity
        return "WITH (UPDLOCK, HOLDLOCK, ROWLOCK)";
    }
}
