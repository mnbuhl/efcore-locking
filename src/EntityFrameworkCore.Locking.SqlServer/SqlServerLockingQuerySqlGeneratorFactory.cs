using EntityFrameworkCore.Locking.Abstractions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Locking.SqlServer;

internal sealed class SqlServerLockingQuerySqlGeneratorFactory : IQuerySqlGeneratorFactory
{
    private readonly QuerySqlGeneratorDependencies _dependencies;
    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private readonly ISqlServerSingletonOptions _sqlServerOptions;
    private readonly ILockSqlGenerator _lockSqlGenerator;

    public SqlServerLockingQuerySqlGeneratorFactory(
        QuerySqlGeneratorDependencies dependencies,
        IRelationalTypeMappingSource typeMappingSource,
        ISqlServerSingletonOptions sqlServerOptions,
        ILockSqlGenerator lockSqlGenerator
    )
    {
        _dependencies = dependencies;
        _typeMappingSource = typeMappingSource;
        _sqlServerOptions = sqlServerOptions;
        _lockSqlGenerator = lockSqlGenerator;
    }

    public QuerySqlGenerator Create() =>
        new SqlServerLockingQuerySqlGenerator(
            _dependencies,
            _typeMappingSource,
            _sqlServerOptions,
            _lockSqlGenerator
        );
}
