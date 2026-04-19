using EntityFrameworkCore.Locking.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Locking.SqlServer;

public static class SqlServerLockingServiceCollectionExtensions
{
    /// <summary>
    /// Adds row-level locking support for SQL Server. Call after UseSqlServer().
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
    /// Adds row-level locking support for SQL Server. Call after UseSqlServer().
    /// </summary>
    public static DbContextOptionsBuilder UseLocking(this DbContextOptionsBuilder optionsBuilder)
    {
        var extension = new LockingOptionsExtension(new SqlServerLockingProvider());
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

        optionsBuilder.ReplaceService<IQuerySqlGeneratorFactory, SqlServerLockingQuerySqlGeneratorFactory>();
        optionsBuilder.AddInterceptors(new LockingValidationInterceptor(), new DistributedLockCleanupInterceptor());

        return optionsBuilder;
    }
}
