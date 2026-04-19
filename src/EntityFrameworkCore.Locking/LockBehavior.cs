namespace EntityFrameworkCore.Locking;

/// <summary>Controls what happens when the requested lock cannot be acquired immediately.</summary>
public enum LockBehavior
{
    /// <summary>Wait indefinitely for the lock (default database behavior).</summary>
    Wait = 0,

    /// <summary>Skip rows that are already locked by another transaction.</summary>
    SkipLocked = 1,

    /// <summary>Fail immediately if any targeted row is locked.</summary>
    NoWait = 2,
}
