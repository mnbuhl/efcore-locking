using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.SqlServer.Query.Internal;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Locking.SqlServer;

/// <summary>
/// Extends SqlServerQuerySqlGenerator to append WITH (UPDLOCK, HOLDLOCK, ROWLOCK) table hints
/// on each table in the FROM clause when a lock is active.
/// SQL Server requires hints on individual tables, not as a trailing clause.
/// </summary>
internal sealed class SqlServerLockingQuerySqlGenerator : SqlServerQuerySqlGenerator
{
    private readonly ILockSqlGenerator _lockSqlGenerator;

    public SqlServerLockingQuerySqlGenerator(
        QuerySqlGeneratorDependencies dependencies,
        IRelationalTypeMappingSource typeMappingSource,
        ISqlServerSingletonOptions sqlServerSingletonOptions,
        ILockSqlGenerator lockSqlGenerator)
        : base(dependencies, typeMappingSource, sqlServerSingletonOptions)
    {
        _lockSqlGenerator = lockSqlGenerator;
    }

    protected override Expression VisitSelect(SelectExpression selectExpression)
    {
        // Validate tag+AsyncLocal consistency before generating SQL
        bool hasLockTag = selectExpression.Tags.Any(t => t.StartsWith(LockTagConstants.Prefix));
        var lockOptions = LockContext.Current;

        if (hasLockTag && lockOptions is null)
            throw new LockingConfigurationException(
                "Lock marker detected in query but LockContext is empty. " +
                "This indicates an AsyncLocal propagation failure. " +
                "Do not compose locking queries across async context boundaries.");

        if (lockOptions is not null && hasLockTag)
            UnsafeShapeDetector.ThrowIfUnsafe(selectExpression);

        return base.VisitSelect(selectExpression);
    }

    protected override Expression VisitTable(TableExpression tableExpression)
    {
        var result = base.VisitTable(tableExpression);

        var lockOptions = LockContext.Current;
        if (lockOptions is null)
            return result;

        if (!_lockSqlGenerator.SupportsLockOptions(lockOptions))
            throw new LockingConfigurationException(
                $"Lock mode {lockOptions.Mode} with behavior {lockOptions.Behavior} is not supported by SQL Server.");

        Sql.Append($" {SqlServerLockSqlGenerator.BuildTableHint(lockOptions)}");
        return result;
    }
}
