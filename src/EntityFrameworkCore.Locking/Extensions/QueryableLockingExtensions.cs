using EntityFrameworkCore.Locking.Internal;

// ReSharper disable once CheckNamespace
namespace EntityFrameworkCore.Locking;

public static class QueryableLockingExtensions
{
    /// <summary>
    /// Acquires an exclusive row-level lock (FOR UPDATE) on the selected rows.
    /// Requires an active EF, explicitly enlisted, or ambient TransactionScope transaction.
    /// </summary>
    /// <exception cref="Exceptions.LockingConfigurationException">
    /// Thrown when the source does not support EF Core async queries, or at query execution time
    /// if no active transaction exists, or if the query shape is
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
        return LockTagApplicator.Apply(source, options);
    }

    /// <summary>
    /// Acquires a shared row-level lock (FOR SHARE) on the selected rows.
    /// Requires an active EF, explicitly enlisted, or ambient TransactionScope transaction.
    /// </summary>
    /// <exception cref="Exceptions.LockingConfigurationException">
    /// Thrown when the source does not support EF Core async queries, or at query execution time
    /// if no active transaction exists, or if the query shape is
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
        return LockTagApplicator.Apply(source, options);
    }
}
