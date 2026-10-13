using System.Data.Common;
using EntityFrameworkCore.Locking.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Locking.Abstractions;

/// <summary>
/// Implemented by each database provider to issue advisory (distributed) lock SQL.
/// Session locks survive transactions and release on disposal or connection close.
/// Transaction locks are owned by the caller's existing transaction and release on completion.
/// </summary>
public interface IAdvisoryLockProvider
{
    /// <summary>Whether this provider supports native transaction-scoped advisory locks.</summary>
    bool SupportsTransactionLocks => false;

    /// <summary>Validates that the provider supports the requested distributed lock mode.</summary>
    /// <param name="mode">The distributed lock mode requested by the caller.</param>
    /// <exception cref="LockingConfigurationException">
    /// Thrown when the requested mode is not supported by the provider.
    /// </exception>
    void ValidateMode(DistributedLockMode mode);

    /// <summary>Acquires an advisory lock asynchronously, waiting until it is available.</summary>
    /// <param name="context">The DbContext that owns the lock registration.</param>
    /// <param name="connection">The database connection that will hold the session-scoped lock.</param>
    /// <param name="key">The lock key.</param>
    /// <param name="timeout">Maximum time to wait, or null to wait indefinitely.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="mode">The distributed lock mode to acquire.</param>
    Task<IDistributedLockHandle> AcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        TimeSpan? timeout,
        CancellationToken ct,
        DistributedLockMode mode
    );

    /// <summary>Attempts to acquire an advisory lock asynchronously without waiting.</summary>
    /// <param name="context">The DbContext that owns the lock registration.</param>
    /// <param name="connection">The database connection that will hold the session-scoped lock.</param>
    /// <param name="key">The lock key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="mode">The distributed lock mode to acquire.</param>
    Task<IDistributedLockHandle?> TryAcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        CancellationToken ct,
        DistributedLockMode mode
    );

    /// <summary>Acquires an advisory lock synchronously, waiting until it is available.</summary>
    /// <param name="context">The DbContext that owns the lock registration.</param>
    /// <param name="connection">The database connection that will hold the session-scoped lock.</param>
    /// <param name="key">The lock key.</param>
    /// <param name="timeout">Maximum time to wait, or null to wait indefinitely.</param>
    /// <param name="mode">The distributed lock mode to acquire.</param>
    IDistributedLockHandle Acquire(
        DbContext context,
        DbConnection connection,
        string key,
        TimeSpan? timeout,
        DistributedLockMode mode
    );

    /// <summary>Attempts to acquire an advisory lock synchronously without waiting.</summary>
    /// <param name="context">The DbContext that owns the lock registration.</param>
    /// <param name="connection">The database connection that will hold the session-scoped lock.</param>
    /// <param name="key">The lock key.</param>
    /// <param name="mode">The distributed lock mode to acquire.</param>
    IDistributedLockHandle? TryAcquire(
        DbContext context,
        DbConnection connection,
        string key,
        DistributedLockMode mode
    );

    /// <summary>Acquires a lock with the requested lifetime, returning its acquisition outcome.</summary>
    /// <remarks>
    /// Transaction scope requires an already-open, caller-owned transaction. Implementations must not
    /// manage the connection or transaction lifecycle. Transaction result release/disposal are no-ops.
    /// The default implementation preserves legacy session providers and rejects other scopes.
    /// </remarks>
    Task<IDistributedLockHandle> AcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        TimeSpan? timeout,
        CancellationToken ct,
        DistributedLockMode mode,
        DistributedLockScope scope
    )
    {
        RequireSessionScope(scope);
        return AcquireAsync(context, connection, key, timeout, ct, mode);
    }

    /// <summary>Attempts a lock without waiting, using the requested lifetime.</summary>
    /// <remarks>Session contention returns null. Transaction contention returns a non-null immutable IsAcquired=false result.</remarks>
    Task<IDistributedLockHandle?> TryAcquireAsync(
        DbContext context,
        DbConnection connection,
        string key,
        CancellationToken ct,
        DistributedLockMode mode,
        DistributedLockScope scope
    )
    {
        RequireSessionScope(scope);
        return TryAcquireAsync(context, connection, key, ct, mode);
    }

    /// <summary>Acquires a lock synchronously with the requested lifetime.</summary>
    /// <remarks>Transaction scope uses an already-open caller-owned transaction; result release/disposal are no-ops.</remarks>
    IDistributedLockHandle Acquire(
        DbContext context,
        DbConnection connection,
        string key,
        TimeSpan? timeout,
        DistributedLockMode mode,
        DistributedLockScope scope
    )
    {
        RequireSessionScope(scope);
        return Acquire(context, connection, key, timeout, mode);
    }

    /// <summary>Attempts a lock synchronously without waiting, using the requested lifetime.</summary>
    /// <remarks>Session contention returns null. Transaction contention returns a non-null immutable IsAcquired=false result.</remarks>
    IDistributedLockHandle? TryAcquire(
        DbContext context,
        DbConnection connection,
        string key,
        DistributedLockMode mode,
        DistributedLockScope scope
    )
    {
        RequireSessionScope(scope);
        return TryAcquire(context, connection, key, mode);
    }

    private static void RequireSessionScope(DistributedLockScope scope)
    {
        if (scope != DistributedLockScope.Session)
            throw new LockingConfigurationException($"The advisory lock provider does not support '{scope}' scope.");
    }
}
