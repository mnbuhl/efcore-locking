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
    private LockOptions? _activeLockOptions;

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
        var previousLockingActive = _lockingActive;
        var previousActiveLockOptions = _activeLockOptions;

        // LastOrDefault: TagWith appends in call order, so the last locking tag is the most recent.
        var lockTag = selectExpression.Tags.LastOrDefault(t =>
            t.StartsWith(LockTagConstants.Prefix, StringComparison.Ordinal)
        );

        if (lockTag is null || !LockTagConstants.TryParse(lockTag, out var lockOptions))
        {
            _lockingActive = false;
            _activeLockOptions = null;
            var innerResult = base.VisitSelect(selectExpression);
            _lockingActive = previousLockingActive;
            _activeLockOptions = previousActiveLockOptions;
            return innerResult;
        }

        UnsafeShapeDetector.ThrowIfUnsafe(selectExpression);
        _lockingActive = true;
        _activeLockOptions = lockOptions;
        var result = base.VisitSelect(selectExpression);
        _lockingActive = previousLockingActive;
        _activeLockOptions = previousActiveLockOptions;
        return result;
    }

    protected override Expression VisitTable(TableExpression tableExpression)
    {
        var result = base.VisitTable(tableExpression);

        if (!_lockingActive || _activeLockOptions is null)
            return result;

        if (!_lockSqlGenerator.SupportsLockOptions(_activeLockOptions))
            throw new LockingConfigurationException(
                $"Lock mode {_activeLockOptions.Mode} with behavior {_activeLockOptions.Behavior} is not supported by SQL Server."
            );

        Sql.AppendLine($" {SqlServerLockSqlGenerator.BuildTableHint(_activeLockOptions)}");
        return result;
    }
}
