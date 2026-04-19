using System.Linq.Expressions;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.SqlServer.Query.Internal;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Locking.SqlServer;

/// <summary>
/// Extends SqlServerQuerySqlGenerator to append WITH (UPDLOCK, HOLDLOCK, ROWLOCK) table hints
/// on each table in the FROM clause when a lock is active.
/// SQL Server requires hints on individual tables, not as a trailing clause.
/// </summary>
internal sealed class SqlServerLockingQuerySqlGenerator : SqlServerQuerySqlGenerator
{
    private readonly ILockSqlGenerator _lockSqlGenerator;
    private bool _lockingActive;

    public SqlServerLockingQuerySqlGenerator(
        QuerySqlGeneratorDependencies dependencies,
        IRelationalTypeMappingSource typeMappingSource,
        ISqlServerSingletonOptions sqlServerSingletonOptions,
        ILockSqlGenerator lockSqlGenerator
    )
        : base(dependencies, typeMappingSource, sqlServerSingletonOptions)
    {
        _lockSqlGenerator = lockSqlGenerator;
    }

    protected override Expression VisitSelect(SelectExpression selectExpression)
    {
        var lockOptions = LockContext.Current;
        var previousLockingActive = _lockingActive;

        var isLockingSelect =
            lockOptions is not null && selectExpression.Tags.Contains(LockTagConstants.BuildTag(lockOptions));

        if (!isLockingSelect)
        {
            _lockingActive = false;
            var innerResult = base.VisitSelect(selectExpression);
            _lockingActive = previousLockingActive;
            return innerResult;
        }

        UnsafeShapeDetector.ThrowIfUnsafe(selectExpression);
        _lockingActive = true;
        var result = base.VisitSelect(selectExpression);
        _lockingActive = previousLockingActive;
        return result;
    }

    protected override Expression VisitTable(TableExpression tableExpression)
    {
        var result = base.VisitTable(tableExpression);

        if (!_lockingActive)
            return result;

        var lockOptions = LockContext.Current;
        if (lockOptions is null)
            return result;

        if (!_lockSqlGenerator.SupportsLockOptions(lockOptions))
            throw new LockingConfigurationException(
                $"Lock mode {lockOptions.Mode} with behavior {lockOptions.Behavior} is not supported by SQL Server."
            );

        Sql.Append($" {SqlServerLockSqlGenerator.BuildTableHint(lockOptions)}");
        return result;
    }
}
