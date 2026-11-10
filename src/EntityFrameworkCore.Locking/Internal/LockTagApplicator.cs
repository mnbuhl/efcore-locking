using System.Linq.Expressions;
using System.Reflection;
using EntityFrameworkCore.Locking.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Locking.Internal;

internal static class LockTagApplicator
{
    private static readonly MethodInfo TagWithMethod = (
        (Func<IQueryable<object>, string, IQueryable<object>>)EntityFrameworkQueryableExtensions.TagWith
    ).Method.GetGenericMethodDefinition();

    internal static IQueryable<T> Apply<T>(IQueryable<T> source, LockOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        var tag = LockTagConstants.BuildTag(options);
        var tagged = source.TagWith(tag);
        if (ReferenceEquals(tagged, source))
        {
            // TagWith silently skips providers other than EF's own EntityQueryProvider.
            // Async wrappers forwarding expressions to EF need the same call added explicitly.
            if (source.Provider is not IAsyncQueryProvider)
                throw new LockingConfigurationException("Row locking requires an EF Core query.");

            tagged = source.Provider.CreateQuery<T>(
                Expression.Call(
                    null,
                    TagWithMethod.MakeGenericMethod(typeof(T)),
                    source.Expression,
                    Expression.Constant(tag)
                )
            );
        }

        // Publish ambient options only after tagging succeeds.
        LockContext.Current = options;
        return tagged;
    }
}
