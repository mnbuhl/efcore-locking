namespace EntityFrameworkCore.Locking.Abstractions;

/// <summary>
/// Handle returned by a successful distributed lock acquisition.
/// Session-scoped: the lock lives until this handle is disposed/released or the connection drops.
/// Release is idempotent — calling Release or disposing multiple times is safe.
/// </summary>
public interface IDistributedLockHandle : IAsyncDisposable, IDisposable
{
    /// <summary>The key this handle holds a lock on.</summary>
    string Key { get; }

    /// <summary>Releases the lock asynchronously. Idempotent.</summary>
    Task ReleaseAsync(CancellationToken cancellationToken = default);

    /// <summary>Releases the lock synchronously. Idempotent. Uses synchronous ADO.NET calls — safe from sync contexts.</summary>
    void Release();
}
