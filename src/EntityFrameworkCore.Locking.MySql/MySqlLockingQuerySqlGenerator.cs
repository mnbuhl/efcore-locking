using System.Linq.Expressions;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure.Internal;
using Pomelo.EntityFrameworkCore.MySql.Query.ExpressionVisitors.Internal;

namespace EntityFrameworkCore.Locking.MySql;

internal sealed class MySqlLockingQuerySqlGenerator : MySqlQuerySqlGenerator
{
    private readonly ILockSqlGenerator _lockSqlGenerator;

    public MySqlLockingQuerySqlGenerator(
        QuerySqlGeneratorDependencies dependencies,
        IRelationalTypeMappingSource typeMappingSource,
        IMySqlOptions options,
        ILockSqlGenerator lockSqlGenerator
    )
        : base(dependencies, typeMappingSource, options)
    {
        _lockSqlGenerator = lockSqlGenerator;
    }

    protected override Expression VisitSelect(SelectExpression selectExpression)
    {
        var result = base.VisitSelect(selectExpression);

        bool hasLockTag = selectExpression.Tags.Any(t => t.StartsWith(LockTagConstants.Prefix));
        var lockOptions = LockContext.Current;

        if (hasLockTag && lockOptions is null)
            throw new LockingConfigurationException(
                "Lock marker detected in query but LockContext is empty. "
                    + "This indicates an AsyncLocal propagation failure. "
                    + "Do not compose locking queries across async context boundaries."
            );

        if (lockOptions is null || !hasLockTag)
            return result;

        UnsafeShapeDetector.ThrowIfUnsafe(selectExpression);

        var clause = _lockSqlGenerator.GenerateLockClause(lockOptions);
        if (clause is not null)
        {
            Sql.AppendLine();
            Sql.Append(clause);
        }

        return result;
    }
}
