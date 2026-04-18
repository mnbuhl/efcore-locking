using EntityFrameworkCore.Locking.Internal;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Locking.PostgreSQL;

/// <summary>
/// PostgreSQL-specific locking extension methods.
/// These modes are not supported by MySQL or SQL Server.
/// </summary>
public static class PostgresQueryableLockingExtensions
{
    /// <summary>
    /// Acquires a FOR NO KEY UPDATE lock on the selected rows.
    /// Blocks other writers but does not block transactions holding FOR KEY SHARE (e.g. FK lookups).
    /// Requires an active transaction on the DbContext.
    /// </summary>
    public static IQueryable<T> ForNoKeyUpdate<T>(
        this IQueryable<T> source,
        LockBehavior behavior = LockBehavior.Wait,
        TimeSpan? timeout = null) where T : class
    {
        var options = new LockOptions { Mode = LockMode.ForNoKeyUpdate, Behavior = behavior, Timeout = timeout };
        LockContext.Current = options;
        return source.TagWith(LockTagConstants.BuildTag(options));
    }

    /// <summary>
    /// Acquires a FOR KEY SHARE lock on the selected rows.
    /// Minimal shared lock — only blocks FOR UPDATE, not FOR NO KEY UPDATE or FOR SHARE.
    /// Intended for FK-referencing queries. Requires an active transaction on the DbContext.
    /// </summary>
    public static IQueryable<T> ForKeyShare<T>(
        this IQueryable<T> source,
        LockBehavior behavior = LockBehavior.Wait,
        TimeSpan? timeout = null) where T : class
    {
        var options = new LockOptions { Mode = LockMode.ForKeyShare, Behavior = behavior, Timeout = timeout };
        LockContext.Current = options;
        return source.TagWith(LockTagConstants.BuildTag(options));
    }
}
