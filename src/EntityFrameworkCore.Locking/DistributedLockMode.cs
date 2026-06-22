namespace EntityFrameworkCore.Locking;

/// <summary>The type of distributed advisory lock to acquire.</summary>
public enum DistributedLockMode
{
    /// <summary>Exclusive lock. Only one holder can acquire the key.</summary>
    Exclusive = 0,

    /// <summary>Shared lock. Multiple shared holders can acquire the key while excluding exclusive holders.</summary>
    Shared = 1,
}
