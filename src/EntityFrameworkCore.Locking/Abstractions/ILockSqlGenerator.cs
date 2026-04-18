namespace EntityFrameworkCore.Locking.Abstractions;

/// <summary>Generates provider-specific SQL fragments for row-level lock clauses.</summary>
public interface ILockSqlGenerator
{
    /// <summary>
    /// Returns the SQL fragment to append (Postgres/MySQL) or inject at table-level (SQL Server).
    /// Returns null if the provider does not support the requested combination.
    /// </summary>
    string? GenerateLockClause(LockOptions options);

    /// <summary>Returns true if this provider supports the given lock options.</summary>
    bool SupportsLockOptions(LockOptions options);

    /// <summary>
    /// Returns pre-statement SQL to execute before the main query (e.g. SET LOCK_TIMEOUT).
    /// Returns null if not needed.
    /// </summary>
    string? GeneratePreStatementSql(LockOptions options);
}
