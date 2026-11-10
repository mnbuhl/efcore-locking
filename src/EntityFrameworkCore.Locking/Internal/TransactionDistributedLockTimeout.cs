using EntityFrameworkCore.Locking.Exceptions;

namespace EntityFrameworkCore.Locking.Internal;

internal static class TransactionDistributedLockTimeout
{
    public static int ToMilliseconds(TimeSpan? timeout)
    {
        if (timeout is null)
            return -1;
        if (timeout.Value < TimeSpan.Zero)
            throw new LockingConfigurationException("Transaction distributed lock timeout must not be negative.");

        return (int)Math.Min(Math.Ceiling(timeout.Value.TotalMilliseconds), int.MaxValue);
    }
}
