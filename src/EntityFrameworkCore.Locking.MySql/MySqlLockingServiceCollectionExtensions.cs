using EntityFrameworkCore.Locking.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Locking.MySql;

public static class MySqlLockingServiceCollectionExtensions
{
    /// <summary>
    /// Adds row-level locking support for MySQL (Pomelo). Call after UseMySql().
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
    /// Adds row-level locking support for MySQL (Pomelo). Call after UseMySql().
    /// </summary>
    public static DbContextOptionsBuilder UseLocking(this DbContextOptionsBuilder optionsBuilder)
    {
        var extension = new LockingOptionsExtension(new MySqlLockingProvider());
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

        optionsBuilder.ReplaceService<IQuerySqlGeneratorFactory, MySqlLockingQuerySqlGeneratorFactory>();
        optionsBuilder.AddInterceptors(new LockingValidationInterceptor(), new DistributedLockCleanupInterceptor());

        return optionsBuilder;
    }
}
