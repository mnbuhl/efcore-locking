using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Locking.Abstractions;

/// <summary>
/// Implemented by each database provider to issue advisory (distributed) lock SQL.
/// Session-scoped: locks survive transactions and are released on dispose or connection close.
/// </summary>
public interface IAdvisoryLockProvider
{
    Task<IDistributedLockHandle> AcquireAsync(DbContext context, DbConnection connection, string key, TimeSpan? timeout, CancellationToken ct);
    Task<IDistributedLockHandle?> TryAcquireAsync(DbContext context, DbConnection connection, string key, CancellationToken ct);
    IDistributedLockHandle Acquire(DbContext context, DbConnection connection, string key, TimeSpan? timeout);
    IDistributedLockHandle? TryAcquire(DbContext context, DbConnection connection, string key);
}
