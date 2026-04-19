using EntityFrameworkCore.Locking.Abstractions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure.Internal;

namespace EntityFrameworkCore.Locking.MySql;

internal sealed class MySqlLockingQuerySqlGeneratorFactory : IQuerySqlGeneratorFactory
{
    private readonly QuerySqlGeneratorDependencies _dependencies;
    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private readonly IMySqlOptions _mySqlOptions;
    private readonly ILockSqlGenerator _lockSqlGenerator;

    public MySqlLockingQuerySqlGeneratorFactory(
        QuerySqlGeneratorDependencies dependencies,
        IRelationalTypeMappingSource typeMappingSource,
        IMySqlOptions mySqlOptions,
        ILockSqlGenerator lockSqlGenerator
    )
    {
        _dependencies = dependencies;
        _typeMappingSource = typeMappingSource;
        _mySqlOptions = mySqlOptions;
        _lockSqlGenerator = lockSqlGenerator;
    }

    public QuerySqlGenerator Create() =>
        new MySqlLockingQuerySqlGenerator(
            _dependencies,
            _typeMappingSource,
            _mySqlOptions,
            _lockSqlGenerator
        );
}
