using EntityFrameworkCore.Locking.Internal;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Locking;

public static class QueryableLockingExtensions
{
    /// <summary>
    /// Acquires an exclusive row-level lock (FOR UPDATE) on the selected rows.
    /// Requires an active transaction on the DbContext.
    /// </summary>
    /// <exception cref="Exceptions.LockingConfigurationException">
    /// Thrown at query execution time if no ambient transaction exists, or if the query shape is
    /// incompatible with row locking: DISTINCT, GROUP BY, set operations (Union/Except/Intersect),
    /// or split queries (AsSplitQuery).
    /// </exception>
    public static IQueryable<T> ForUpdate<T>(
        this IQueryable<T> source,
        LockBehavior behavior = LockBehavior.Wait,
        TimeSpan? timeout = null
    )
        where T : class
    {
        var options = new LockOptions
        {
            Mode = LockMode.ForUpdate,
            Behavior = behavior,
            Timeout = timeout,
        };
        LockContext.Current = options;
        return source.TagWith(LockTagConstants.BuildTag(options));
    }

    /// <summary>
    /// Acquires a shared row-level lock (FOR SHARE) on the selected rows.
    /// Requires an active transaction on the DbContext.
    /// </summary>
    /// <exception cref="Exceptions.LockingConfigurationException">
    /// Thrown at query execution time if no ambient transaction exists, or if the query shape is
    /// incompatible with row locking: DISTINCT, GROUP BY, set operations (Union/Except/Intersect),
    /// or split queries (AsSplitQuery).
    /// </exception>
    public static IQueryable<T> ForShare<T>(
        this IQueryable<T> source,
        LockBehavior behavior = LockBehavior.Wait,
        TimeSpan? timeout = null
    )
        where T : class
    {
        var options = new LockOptions
        {
            Mode = LockMode.ForShare,
            Behavior = behavior,
            Timeout = timeout,
        };
        LockContext.Current = options;
        return source.TagWith(LockTagConstants.BuildTag(options));
    }
}
