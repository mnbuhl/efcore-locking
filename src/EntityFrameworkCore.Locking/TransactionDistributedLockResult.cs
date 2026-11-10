using EntityFrameworkCore.Locking.Abstractions;

namespace EntityFrameworkCore.Locking;

/// <summary>The immutable outcome of a transaction-scoped distributed lock acquisition attempt.</summary>
/// <remarks>
/// This result does not own or release the lock. The transaction owns its lifetime.
/// <see cref="IsAcquired"/> records the attempt's outcome and does not change when the transaction ends.
/// </remarks>
public sealed class TransactionDistributedLockResult : IDistributedLockHandle
{
    internal TransactionDistributedLockResult(string key, bool isAcquired)
    {
        Key = key;
        IsAcquired = isAcquired;
    }

    /// <summary>The key requested by the acquisition attempt.</summary>
    public string Key { get; }

    /// <summary>Whether the acquisition attempt succeeded, irrespective of later transaction completion.</summary>
    public bool IsAcquired { get; }

    /// <summary>A no-op; the transaction owns the lock's lifetime.</summary>
    public Task ReleaseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>A no-op; the transaction owns the lock's lifetime.</summary>
    public void Release() { }

    /// <summary>A no-op; the transaction owns the lock's lifetime.</summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>A no-op; the transaction owns the lock's lifetime.</summary>
    public void Dispose() { }
}
