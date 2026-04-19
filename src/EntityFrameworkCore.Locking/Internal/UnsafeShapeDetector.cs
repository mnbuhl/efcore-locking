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
    }
}
