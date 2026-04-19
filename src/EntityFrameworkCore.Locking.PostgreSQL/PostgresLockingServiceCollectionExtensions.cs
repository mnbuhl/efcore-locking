using EntityFrameworkCore.Locking.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Locking.PostgreSQL;

public static class PostgresLockingServiceCollectionExtensions
{
    /// <summary>
    /// Adds row-level locking support for PostgreSQL (Npgsql). Call after UseNpgsql().
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
    /// Adds row-level locking support for PostgreSQL (Npgsql). Call after UseNpgsql().
    /// </summary>
    public static DbContextOptionsBuilder UseLocking(this DbContextOptionsBuilder optionsBuilder)
    {
        var extension = new LockingOptionsExtension(new PostgresLockingProvider());
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

        optionsBuilder.ReplaceService<
            IQuerySqlGeneratorFactory,
            PostgresLockingQuerySqlGeneratorFactory
        >();
        optionsBuilder.AddInterceptors(
            new LockingValidationInterceptor(),
            new DistributedLockCleanupInterceptor()
        );

        return optionsBuilder;
    }
}
