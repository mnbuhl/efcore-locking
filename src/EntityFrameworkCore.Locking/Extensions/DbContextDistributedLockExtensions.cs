using System.Data;
using System.Data.Common;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Locking;

/// <summary>
/// Extension methods on <see cref="DbContext"/> for acquiring distributed (advisory) locks.
/// No active transaction is required — locks are session-scoped.
/// </summary>
public static class DbContextDistributedLockExtensions
{
    /// <summary>
    /// Acquires a distributed lock with the given key, blocking until it is available.
    /// </summary>
    /// <param name="ctx">The DbContext whose connection will hold the lock.</param>
    /// <param name="key">Lock key (1–255 characters).</param>
    /// <param name="timeout">Maximum time to wait. Throws <see cref="LockTimeoutException"/> if exceeded. Null = wait indefinitely.</param>
    /// <param name="ct">Cancellation token. Cancellation is best-effort (driver-dependent).</param>
    public static async Task<IDistributedLockHandle> AcquireDistributedLockAsync(
        this DbContext ctx,
        string key,
        TimeSpan? timeout = null,
        CancellationToken ct = default
    )
    {
        var (provider, connection, openedByMe) = await PrepareAsync(ctx, key, ct).ConfigureAwait(false);
        try
        {
            DistributedLockRegistry.RegisterOrThrow(ctx, connection, key);
            try
            {
                return await provider.AcquireAsync(ctx, connection, key, timeout, ct).ConfigureAwait(false);
            }
            catch
            {
                DistributedLockRegistry.Unregister(ctx, connection, key);
                throw;
            }
        }
        catch
        {
            if (openedByMe)
                await connection.CloseAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Attempts to acquire a distributed lock without blocking.
    /// Returns null immediately if the lock is held by another connection.
    /// </summary>
    public static async Task<IDistributedLockHandle?> TryAcquireDistributedLockAsync(
        this DbContext ctx,
        string key,
        CancellationToken ct = default
    )
    {
        var (provider, connection, openedByMe) = await PrepareAsync(ctx, key, ct).ConfigureAwait(false);
        try
        {
            DistributedLockRegistry.RegisterOrThrow(ctx, connection, key);
            IDistributedLockHandle? handle;
            try
            {
                handle = await provider.TryAcquireAsync(ctx, connection, key, ct).ConfigureAwait(false);
            }
            catch
            {
                DistributedLockRegistry.Unregister(ctx, connection, key);
                throw;
            }
            if (handle is null)
            {
                DistributedLockRegistry.Unregister(ctx, connection, key);
                if (openedByMe)
                    await connection.CloseAsync().ConfigureAwait(false);
            }
            return handle;
        }
        catch
        {
            if (openedByMe)
                await connection.CloseAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Acquires a distributed lock synchronously.</summary>
    public static IDistributedLockHandle AcquireDistributedLock(
        this DbContext ctx,
        string key,
        TimeSpan? timeout = null
    )
    {
        var (provider, connection, openedByMe) = PrepareSync(ctx, key);
        try
        {
            DistributedLockRegistry.RegisterOrThrow(ctx, connection, key);
            try
            {
                return provider.Acquire(ctx, connection, key, timeout);
            }
            catch
            {
                DistributedLockRegistry.Unregister(ctx, connection, key);
                throw;
            }
        }
        catch
        {
            if (openedByMe)
                connection.Close();
            throw;
        }
    }

    /// <summary>Attempts to acquire a distributed lock synchronously. Returns null if contested.</summary>
    public static IDistributedLockHandle? TryAcquireDistributedLock(this DbContext ctx, string key)
    {
        var (provider, connection, openedByMe) = PrepareSync(ctx, key);
        try
        {
            DistributedLockRegistry.RegisterOrThrow(ctx, connection, key);
            IDistributedLockHandle? handle;
            try
            {
                handle = provider.TryAcquire(ctx, connection, key);
            }
            catch
            {
                DistributedLockRegistry.Unregister(ctx, connection, key);
                throw;
            }
            if (handle is null)
            {
                DistributedLockRegistry.Unregister(ctx, connection, key);
                if (openedByMe)
                    connection.Close();
            }
            return handle;
        }
        catch
        {
            if (openedByMe)
                connection.Close();
            throw;
        }
    }

    /// <summary>Returns true if the current DbContext's provider supports distributed locks.</summary>
    public static bool SupportsDistributedLocks(this DbContext ctx)
    {
        var lp = ctx.GetInfrastructure().GetService<ILockingProvider>();
        return lp?.AdvisoryLockProvider is not null;
    }

    private static async Task<(IAdvisoryLockProvider provider, DbConnection connection, bool openedByMe)> PrepareAsync(
        DbContext ctx,
        string key,
        CancellationToken ct
    )
    {
        ValidateKey(key);
        var provider = ResolveProvider(ctx);
        var connection = ctx.Database.GetDbConnection();
        bool openedByMe = false;
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            openedByMe = true;
        }
        return (provider, connection, openedByMe);
    }

    private static (IAdvisoryLockProvider provider, DbConnection connection, bool openedByMe) PrepareSync(
        DbContext ctx,
        string key
    )
    {
        ValidateKey(key);
        var provider = ResolveProvider(ctx);
        var connection = ctx.Database.GetDbConnection();
        bool openedByMe = false;
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
            openedByMe = true;
        }
        return (provider, connection, openedByMe);
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Lock key must not be null or empty.", nameof(key));
        if (key.Length > 255)
            throw new ArgumentException("Lock key must not exceed 255 characters.", nameof(key));
    }

    private static IAdvisoryLockProvider ResolveProvider(DbContext ctx)
    {
        var lp = ctx.GetInfrastructure().GetService<ILockingProvider>();
        if (lp is null)
            throw new LockingConfigurationException(
                "No ILockingProvider is registered. Call UseLocking() when configuring the DbContext."
            );
        if (lp.AdvisoryLockProvider is null)
            throw new LockingConfigurationException(
                $"The '{lp.ProviderName}' provider does not support distributed locks."
            );
        return lp.AdvisoryLockProvider;
    }
}
