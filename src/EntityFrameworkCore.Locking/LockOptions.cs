namespace EntityFrameworkCore.Locking;

/// <summary>Immutable options bag that fully describes a lock request.</summary>
public sealed record LockOptions
{
    public LockMode Mode { get; init; }
    public LockBehavior Behavior { get; init; } = LockBehavior.Wait;

    /// <summary>
    /// Maximum time to wait for the lock. Only meaningful when <see cref="Behavior"/> is
    /// <see cref="LockBehavior.Wait"/>. Null means wait indefinitely.
    /// Not all providers support per-query timeout; those that do not throw
    /// <see cref="Exceptions.LockingConfigurationException"/>.
    /// </summary>
    public TimeSpan? Timeout { get; init; }
}
