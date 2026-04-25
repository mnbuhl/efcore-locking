using EntityFrameworkCore.Locking.Exceptions;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Locking.Internal;

internal static class UnsafeShapeDetector
{
    internal static void ThrowIfUnsafe(SelectExpression selectExpression)
    {
        // Set operations: Union, Except, Intersect appear as SetOperationBase in the Tables collection
        foreach (var table in selectExpression.Tables)
        {
            if (table is SetOperationBase)
                throw new LockingConfigurationException(
                    "ForUpdate/ForShare is not compatible with set operations (Union/Except/Intersect)."
                );
        }

        // Split query: EF Core marks split queries with a specific tag
        if (selectExpression.Tags.Any(t => t.Contains("SplitQuery") || t.Contains("split_query")))
            throw new LockingConfigurationException(
                "ForUpdate/ForShare is not compatible with split queries. Remove AsSplitQuery()."
            );

        if (selectExpression.IsDistinct)
            throw new LockingConfigurationException("ForUpdate/ForShare is not compatible with DISTINCT queries.");

        // Aggregate terminal ops (CountAsync, SumAsync, MaxAsync, MinAsync, LongCountAsync) produce
        // a scalar aggregate function in the outer projection. Row-level locking a scalar is meaningless.
        // AnyAsync is safe: EF Core translates it to a scalar subquery with no outer aggregate function.
        if (
            selectExpression.Projection.Any(p =>
                p.Expression is Microsoft.EntityFrameworkCore.Query.SqlExpressions.SqlFunctionExpression func
                && _aggregateFunctionNames.Contains(func.Name)
            )
        )
            throw new LockingConfigurationException(
                "ForUpdate/ForShare is not compatible with aggregate queries (CountAsync, SumAsync, MaxAsync, MinAsync, LongCountAsync)."
            );

        // GroupBy is not checked: ForUpdate<T> requires T : class, so EF Core always translates
        // GroupBy results into correlated subqueries — GroupBy never appears on the outer SELECT.
    }

    private static readonly System.Collections.Generic.HashSet<string> _aggregateFunctionNames = new(
        System.StringComparer.OrdinalIgnoreCase
    )
    {
        "COUNT",
        "COUNT_BIG",
        "SUM",
        "MAX",
        "MIN",
        "AVG",
    };
}
