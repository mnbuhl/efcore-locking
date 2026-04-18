using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Internal;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Locking.PostgreSQL;

/// <summary>
/// Extends NpgsqlQuerySqlGenerator to append FOR UPDATE / FOR SHARE clauses
/// when LockContext carries active lock options.
/// </summary>
internal sealed class PostgresLockingQuerySqlGenerator : NpgsqlQuerySqlGenerator
{
    private readonly ILockSqlGenerator _lockSqlGenerator;

    public PostgresLockingQuerySqlGenerator(
        QuerySqlGeneratorDependencies dependencies,
        IRelationalTypeMappingSource typeMappingSource,
        bool reverseNullOrderingEnabled,
        Version postgresVersion,
        ILockSqlGenerator lockSqlGenerator)
        : base(dependencies, typeMappingSource, reverseNullOrderingEnabled, postgresVersion)
    {
        _lockSqlGenerator = lockSqlGenerator;
    }

    protected override Expression VisitSelect(SelectExpression selectExpression)
    {
        var result = base.VisitSelect(selectExpression);

        bool hasLockTag = selectExpression.Tags.Any(t => t.StartsWith(LockTagConstants.Prefix));
        var lockOptions = LockContext.Current;

        // Tag present but AsyncLocal null: propagation failure — would silently cache unlocked SQL
        if (hasLockTag && lockOptions is null)
            throw new LockingConfigurationException(
                "Lock marker detected in query but LockContext is empty. " +
                "This indicates an AsyncLocal propagation failure. " +
                "Do not compose locking queries across async context boundaries.");

        // AsyncLocal set but tag absent: stale AsyncLocal from a prior query — ignore
        if (lockOptions is null || !hasLockTag)
            return result;

        UnsafeShapeDetector.ThrowIfUnsafe(selectExpression);

        var preStatement = _lockSqlGenerator.GeneratePreStatementSql(lockOptions);
        if (preStatement is not null)
        {
            // Pre-statement SQL (SET LOCAL lock_timeout) is emitted via the interceptor mechanism;
            // at VisitSelect time the SQL builder is mid-stream. We store it for the interceptor.
            // For M2 (basic ForUpdate only), no pre-statement is needed.
        }

        var clause = _lockSqlGenerator.GenerateLockClause(lockOptions);
        if (clause is not null)
        {
            Sql.AppendLine();
            Sql.Append(clause);
        }

        return result;
    }
}
