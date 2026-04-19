using System.Linq.Expressions;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Internal;

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
        ILockSqlGenerator lockSqlGenerator
    )
        : base(dependencies, typeMappingSource, reverseNullOrderingEnabled, postgresVersion)
    {
        _lockSqlGenerator = lockSqlGenerator;
    }

    protected override Expression VisitSelect(SelectExpression selectExpression)
    {
        var result = base.VisitSelect(selectExpression);

        var lockOptions = LockContext.Current;

        if (
            lockOptions is null
            || !selectExpression.Tags.Contains(LockTagConstants.BuildTag(lockOptions))
        )
            return result;

        UnsafeShapeDetector.ThrowIfUnsafe(selectExpression);

        // When the query has LEFT JOINs (e.g. collection Includes), a bare FOR UPDATE/SHARE
        // is rejected by PostgreSQL ("cannot be applied to the nullable side of an outer join").
        // FOR UPDATE OF "alias" scopes the lock to the root table only, which is valid.
        var hasLeftJoin = selectExpression.Tables.Skip(1).OfType<LeftJoinExpression>().Any();
        var rootAlias = hasLeftJoin ? selectExpression.Tables[0].Alias : null;

        var clause = BuildLockClause(lockOptions!, rootAlias);
        if (clause is not null)
        {
            Sql.AppendLine();
            Sql.Append(clause);
        }

        return result;
    }

    private string? BuildLockClause(LockOptions lockOptions, string? tableAlias)
    {
        var clause = _lockSqlGenerator.GenerateLockClause(lockOptions);
        if (clause is null || tableAlias is null)
            return clause;

        // Insert OF "alias" between mode and modifier (SKIP LOCKED / NOWAIT) for left-join queries.
        // PostgreSQL rejects bare FOR UPDATE/SHARE on the nullable side of an outer join.
        var modifierIndex = clause.IndexOf(" SKIP LOCKED", StringComparison.Ordinal);
        if (modifierIndex < 0)
            modifierIndex = clause.IndexOf(" NOWAIT", StringComparison.Ordinal);

        return modifierIndex < 0
            ? $"{clause} OF \"{tableAlias}\""
            : $"{clause[..modifierIndex]} OF \"{tableAlias}\"{clause[modifierIndex..]}";
    }
}
