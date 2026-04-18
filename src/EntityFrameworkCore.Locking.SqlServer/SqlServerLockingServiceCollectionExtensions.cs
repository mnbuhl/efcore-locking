using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Locking.SqlServer;

public static class SqlServerLockingServiceCollectionExtensions
{
    /// <summary>
    /// Adds row-level locking support for SQL Server. Call after UseSqlServer().
    /// </summary>
    public static DbContextOptionsBuilder UseLocking(this DbContextOptionsBuilder optionsBuilder)
    {
        // M6: register ILockingProvider, replace IQuerySqlGeneratorFactory, add LockingValidationInterceptor
        return optionsBuilder;
    }
}
