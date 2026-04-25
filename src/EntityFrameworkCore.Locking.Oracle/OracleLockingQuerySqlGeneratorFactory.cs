using EntityFrameworkCore.Locking.Abstractions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Oracle.EntityFrameworkCore.Infrastructure.Internal;

namespace EntityFrameworkCore.Locking.Oracle;

internal sealed class OracleLockingQuerySqlGeneratorFactory : IQuerySqlGeneratorFactory
{
    private readonly QuerySqlGeneratorDependencies _dependencies;
    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private readonly IOracleOptions _oracleOptions;
    private readonly ILockSqlGenerator _lockSqlGenerator;

    public OracleLockingQuerySqlGeneratorFactory(
        QuerySqlGeneratorDependencies dependencies,
        IRelationalTypeMappingSource typeMappingSource,
        IOracleOptions oracleOptions,
        ILockSqlGenerator lockSqlGenerator
    )
    {
        _dependencies = dependencies;
        _typeMappingSource = typeMappingSource;
        _oracleOptions = oracleOptions;
        _lockSqlGenerator = lockSqlGenerator;
    }

    public QuerySqlGenerator Create() =>
        new OracleLockingQuerySqlGenerator(
            _dependencies,
            _typeMappingSource,
            _oracleOptions.OracleSQLCompatibility,
            _lockSqlGenerator
        );
}
