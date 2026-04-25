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

        // LastOrDefault: TagWith appends in call order, so the last locking tag is the most recent.
        var lockTag = selectExpression.Tags.LastOrDefault(t =>
            t.StartsWith(LockTagConstants.Prefix, StringComparison.Ordinal)
        );

        if (lockTag is null || !LockTagConstants.TryParse(lockTag, out var lockOptions))
            return result;

        UnsafeShapeDetector.ThrowIfUnsafe(selectExpression);

        var clause = _lockSqlGenerator.GenerateLockClause(lockOptions!);
        if (clause is not null)
        {
            Sql.AppendLine().AppendLine(clause);
        }

        return result;
    }
}
