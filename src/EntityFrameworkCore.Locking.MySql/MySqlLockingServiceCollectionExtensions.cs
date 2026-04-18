using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Locking.MySql;

public static class MySqlLockingServiceCollectionExtensions
{
    /// <summary>
    /// Adds row-level locking support for MySQL (Pomelo). Call after UseMySql().
    /// </summary>
    public static DbContextOptionsBuilder UseLocking(this DbContextOptionsBuilder optionsBuilder)
    {
        // M4: register ILockingProvider, replace IQuerySqlGeneratorFactory, add LockingValidationInterceptor
        return optionsBuilder;
    }
}
