namespace EntityFrameworkCore.Locking;

/// <summary>The lifetime of a distributed advisory lock.</summary>
public enum DistributedLockScope
{
    /// <summary>The lock is released by the handle or when its connection closes.</summary>
    Session = 0,

    /// <summary>The caller's existing transaction owns the lock until completion.</summary>
    Transaction = 1,
}
