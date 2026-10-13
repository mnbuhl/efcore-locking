namespace EntityFrameworkCore.Locking;

/// <summary>The immutable outcome of a transaction-scoped distributed lock acquisition attempt.</summary>
/// <remarks>
/// This result does not own or release the lock. The transaction owns its lifetime.
/// <see cref="IsAcquired"/> records the attempt's outcome and does not change when the transaction ends.
/// </remarks>
public sealed class TransactionDistributedLockResult
{
    internal TransactionDistributedLockResult(bool isAcquired)
    {
        IsAcquired = isAcquired;
    }

    /// <summary>Whether the acquisition attempt succeeded, irrespective of later transaction completion.</summary>
    public bool IsAcquired { get; }
}
