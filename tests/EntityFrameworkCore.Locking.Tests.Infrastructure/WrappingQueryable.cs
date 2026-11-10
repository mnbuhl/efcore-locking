using System.Collections;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Locking.Tests.Infrastructure;

// Keeps a custom provider through composition, forwarding the expressions unchanged to EF.
public sealed class WrappingQueryable<T>(IQueryable<T> inner) : IOrderedQueryable<T>, IAsyncEnumerable<T>
{
    public Type ElementType => typeof(T);
    public Expression Expression => inner.Expression;
    public IQueryProvider Provider { get; } = new WrappingQueryProvider(inner.Provider);

    public IEnumerator<T> GetEnumerator() => inner.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        ((IAsyncEnumerable<T>)inner).GetAsyncEnumerator(cancellationToken);

    private sealed class WrappingQueryProvider(IQueryProvider provider) : IAsyncQueryProvider
    {
        public IQueryable CreateQuery(Expression expression)
        {
            var query = provider.CreateQuery(expression);
            return (IQueryable)(
                Activator.CreateInstance(typeof(WrappingQueryable<>).MakeGenericType(query.ElementType), query)
                ?? throw new InvalidOperationException("Could not create wrapping query.")
            );
        }

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
            new WrappingQueryable<TElement>(provider.CreateQuery<TElement>(expression));

        public object? Execute(Expression expression) => provider.Execute(expression);

        public TResult Execute<TResult>(Expression expression) => provider.Execute<TResult>(expression);

        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default) =>
            ((IAsyncQueryProvider)provider).ExecuteAsync<TResult>(expression, cancellationToken);
    }
}
