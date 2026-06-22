using System.Data.Common;
using EntityFrameworkCore.Locking.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Locking.Abstractions;

/// <summary>
/// Implemented by each database provider to issue advisory (distributed) lock SQL.
/// Session-scoped: locks survive transactions and are released on dispose or connection close.
/// </summary>
public interface IAdvisoryLockProvider
{
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
}
