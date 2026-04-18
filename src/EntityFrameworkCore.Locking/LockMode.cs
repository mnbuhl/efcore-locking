namespace EntityFrameworkCore.Locking;

/// <summary>The type of row-level lock to acquire.</summary>
public enum LockMode
{
    /// <summary>Exclusive row lock (FOR UPDATE / UPDLOCK).</summary>
    ForUpdate = 0,

    /// <summary>Shared row lock (FOR SHARE / HOLDLOCK).</summary>
    ForShare = 1
}
