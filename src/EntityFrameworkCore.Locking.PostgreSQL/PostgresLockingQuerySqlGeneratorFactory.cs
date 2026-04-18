using EntityFrameworkCore.Locking.Abstractions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;

namespace EntityFrameworkCore.Locking.PostgreSQL;

internal sealed class PostgresLockingQuerySqlGeneratorFactory : IQuerySqlGeneratorFactory
{
    private readonly QuerySqlGeneratorDependencies _dependencies;
    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private readonly INpgsqlSingletonOptions _npgsqlOptions;
    private readonly ILockSqlGenerator _lockSqlGenerator;

    public PostgresLockingQuerySqlGeneratorFactory(
        QuerySqlGeneratorDependencies dependencies,
        IRelationalTypeMappingSource typeMappingSource,
        INpgsqlSingletonOptions npgsqlOptions,
        ILockSqlGenerator lockSqlGenerator)
    {
        _dependencies = dependencies;
        _typeMappingSource = typeMappingSource;
        _npgsqlOptions = npgsqlOptions;
        _lockSqlGenerator = lockSqlGenerator;
    }

    public QuerySqlGenerator Create()
        => new PostgresLockingQuerySqlGenerator(
            _dependencies,
            _typeMappingSource,
            _npgsqlOptions.ReverseNullOrderingEnabled,
            _npgsqlOptions.PostgresVersion,
            _lockSqlGenerator);
}
