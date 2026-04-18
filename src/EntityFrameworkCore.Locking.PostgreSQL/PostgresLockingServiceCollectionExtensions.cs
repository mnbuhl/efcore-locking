using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EntityFrameworkCore.Locking.PostgreSQL;

public static class PostgresLockingServiceCollectionExtensions
{
    /// <summary>
    /// Adds row-level locking support for PostgreSQL (Npgsql). Call after UseNpgsql().
    /// </summary>
    public static DbContextOptionsBuilder UseLocking(this DbContextOptionsBuilder optionsBuilder)
    {
        // M2: register ILockingProvider, replace IQuerySqlGeneratorFactory, add LockingValidationInterceptor
        return optionsBuilder;
    }
}
