using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Locking.Abstractions;

/// <summary>
/// Acquires native transaction-scoped advisory locks on an already-open, caller-owned connection.
/// Implementations must not open, close, enlist, create or complete transactions, or release acquired locks.
/// </summary>
public interface ITransactionAdvisoryLockProvider
{
    /// <summary>Validates support for the requested lock mode.</summary>
    void ValidateMode(DistributedLockMode mode);

    /// <summary>Acquires a lock asynchronously in the caller's transaction.</summary>
    Task AcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        TimeSpan? timeout,
        CancellationToken ct,
        DistributedLockMode mode
    );

    /// <summary>Attempts acquisition asynchronously without waiting; false means ordinary contention.</summary>
    Task<bool> TryAcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        CancellationToken ct,
        DistributedLockMode mode
    );

    /// <summary>Acquires a lock synchronously in the caller's transaction.</summary>
    void Acquire(DbContext context, DbConnection connection, string key, TimeSpan? timeout, DistributedLockMode mode);

    /// <summary>Attempts acquisition synchronously without waiting; false means ordinary contention.</summary>
    bool TryAcquire(DbContext context, DbConnection connection, string key, DistributedLockMode mode);
}
