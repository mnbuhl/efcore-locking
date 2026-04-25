using System.Linq.Expressions;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Oracle.EntityFrameworkCore.Query.Sql.Internal;

namespace EntityFrameworkCore.Locking.Oracle;

/// <summary>
/// Extends OracleQuerySqlGenerator to append FOR UPDATE clauses when LockContext carries active lock options.
/// </summary>
internal sealed class OracleLockingQuerySqlGenerator : OracleQuerySqlGenerator
{
    private readonly ILockSqlGenerator _lockSqlGenerator;

    public OracleLockingQuerySqlGenerator(
        QuerySqlGeneratorDependencies dependencies,
        IRelationalTypeMappingSource typeMappingSource,
        OracleSQLCompatibility oracleSQLCompatibility,
        ILockSqlGenerator lockSqlGenerator
    )
        : base(dependencies, typeMappingSource, oracleSQLCompatibility)
    {
        _lockSqlGenerator = lockSqlGenerator;
    }

    protected override Expression VisitSelect(SelectExpression selectExpression)
    {
        var result = base.VisitSelect(selectExpression);

        var lockOptions = LockContext.Current;

        if (lockOptions is null || !selectExpression.Tags.Contains(LockTagConstants.BuildTag(lockOptions)))
            return result;

        UnsafeShapeDetector.ThrowIfUnsafe(selectExpression);

        var clause = _lockSqlGenerator.GenerateLockClause(lockOptions!);
        if (clause is not null)
        {
            Sql.AppendLine();
            Sql.Append(clause);
        }

        return result;
    }
}
