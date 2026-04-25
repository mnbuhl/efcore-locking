using EntityFrameworkCore.Locking.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Locking.Oracle;

public static class OracleLockingServiceCollectionExtensions
{
    /// <summary>
    /// Adds row-level locking support for Oracle. Call after UseOracle().
    /// </summary>
    public static DbContextOptionsBuilder<TContext> UseLocking<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder
    )
        where TContext : DbContext
    {
        ((DbContextOptionsBuilder)optionsBuilder).UseLocking();
        return optionsBuilder;
    }

    /// <summary>
    /// Adds row-level locking support for Oracle. Call after UseOracle().
    /// </summary>
    public static DbContextOptionsBuilder UseLocking(this DbContextOptionsBuilder optionsBuilder)
    {
        var extension = new LockingOptionsExtension(new OracleLockingProvider());
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

        optionsBuilder.ReplaceService<IQuerySqlGeneratorFactory, OracleLockingQuerySqlGeneratorFactory>();
        optionsBuilder.AddInterceptors(new LockingValidationInterceptor(), new DistributedLockCleanupInterceptor());

        return optionsBuilder;
    }
}
