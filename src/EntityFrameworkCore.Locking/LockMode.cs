namespace EntityFrameworkCore.Locking;

/// <summary>The type of row-level lock to acquire.</summary>
public enum LockMode
{
    /// <summary>Exclusive row lock (FOR UPDATE / UPDLOCK).</summary>
    ForUpdate = 0,

    /// <summary>Shared row lock (FOR SHARE / HOLDLOCK).</summary>
    ForShare = 1,

    /// <summary>
    /// PostgreSQL-only. Blocks writers but allows FK-referencing readers (FOR KEY SHARE).
    /// Weaker than FOR UPDATE — does not prevent key-share locks on the same row.
    /// </summary>
    ForNoKeyUpdate = 2,

    /// <summary>
    /// PostgreSQL-only. Minimal shared lock that only blocks FOR UPDATE.
    /// Intended for FK lookups; does not block FOR NO KEY UPDATE or FOR SHARE.
    /// </summary>
    ForKeyShare = 3,
}
