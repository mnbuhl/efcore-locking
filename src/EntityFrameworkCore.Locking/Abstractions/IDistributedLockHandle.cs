namespace EntityFrameworkCore.Locking.Abstractions;

/// <summary>
/// Result of a distributed lock acquisition.
/// Session-scoped: the lock lives until this handle is disposed/released or the connection drops.
/// Transaction-scoped: the transaction owns the lifetime, and release/disposal are no-ops.
/// Release is idempotent — calling Release or disposing multiple times is safe.
/// </summary>
public interface IDistributedLockHandle : IAsyncDisposable, IDisposable
{
    /// <summary>The key requested by the acquisition attempt.</summary>
    string Key { get; }

    /// <summary>Whether the acquisition attempt succeeded. This does not track later release or transaction completion.</summary>
    bool IsAcquired => true;

    /// <summary>Releases a session lock asynchronously. Idempotent; a no-op for transaction locks.</summary>
    Task ReleaseAsync(CancellationToken cancellationToken = default);

    /// <summary>Releases a session lock synchronously. Idempotent; a no-op for transaction locks.</summary>
    void Release();
}
