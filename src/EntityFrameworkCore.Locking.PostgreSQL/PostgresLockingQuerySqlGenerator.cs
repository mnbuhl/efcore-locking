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

        // When the query has LEFT JOINs (e.g. collection Includes), a bare FOR UPDATE/SHARE
        // is rejected by PostgreSQL ("cannot be applied to the nullable side of an outer join").
        // FOR UPDATE OF "alias" scopes the lock to the root table only, which is valid.
        var hasLeftJoin = selectExpression.Tables.Skip(1).OfType<LeftJoinExpression>().Any();
        var rootAlias = hasLeftJoin ? selectExpression.Tables[0].Alias : null;

        var clause = BuildLockClause(lockOptions, rootAlias);
        if (clause is not null)
        {
            Sql.AppendLine();
            Sql.Append(clause);
        }

        return result;
    }

    private static string BuildLockClause(LockOptions lockOptions, string? tableAlias)
    {
        var mode = lockOptions.Mode switch
        {
            LockMode.ForUpdate => "FOR UPDATE",
            LockMode.ForShare => "FOR SHARE",
            LockMode.ForNoKeyUpdate => "FOR NO KEY UPDATE",
            LockMode.ForKeyShare => "FOR KEY SHARE",
            _ => throw new LockingConfigurationException($"Unsupported lock mode: {lockOptions.Mode}")
        };

        var of = tableAlias is not null ? $" OF \"{tableAlias}\"" : string.Empty;

        var modifier = lockOptions.Behavior switch
        {
            LockBehavior.Wait => string.Empty,
            LockBehavior.SkipLocked => " SKIP LOCKED",
            LockBehavior.NoWait => " NOWAIT",
            _ => throw new LockingConfigurationException($"Unsupported lock behavior: {lockOptions.Behavior}")
        };

        return $"{mode}{of}{modifier}";
    }
}
