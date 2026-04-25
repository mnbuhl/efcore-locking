using System.Data;
using System.Data.Common;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Locking;

/// <summary>
/// Extension methods on <see cref="DatabaseFacade"/> (accessed via <c>DbContext.Database</c>) for
/// acquiring distributed (advisory) locks. No active transaction is required — locks are session-scoped.
/// </summary>
public static class DatabaseFacadeDistributedLockExtensions
{
    /// <summary>
    /// Acquires a distributed lock with the given key, blocking until it is available.
    /// </summary>
    /// <param name="database">The <see cref="DatabaseFacade"/> whose connection will hold the lock.</param>
    /// <param name="key">Lock key (1–255 characters).</param>
    /// <param name="timeout">Maximum time to wait. Throws <see cref="LockTimeoutException"/> if exceeded. Null = wait indefinitely.</param>
    /// <param name="ct">Cancellation token. Cancellation is best-effort (driver-dependent).</param>
    /// <exception cref="LockingConfigurationException">
    /// Thrown if the key is null, empty, or longer than 255 characters; if no locking provider is
    /// registered; or if the provider does not support distributed locks.
    /// </exception>
    /// <exception cref="LockTimeoutException">
    /// Thrown if <paramref name="timeout"/> is exceeded before the lock can be acquired.
    /// </exception>
    public static async Task<IDistributedLockHandle> AcquireDistributedLockAsync(
        this DatabaseFacade database,
        string key,
        TimeSpan? timeout = null,
        CancellationToken ct = default
    )
    {
        var (ctx, provider, connection, openedByMe) = await PrepareAsync(database, key, ct).ConfigureAwait(false);
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
    /// <exception cref="LockingConfigurationException">
    /// Thrown if the key is null, empty, or longer than 255 characters; if no locking provider is
    /// registered; or if the provider does not support distributed locks.
    /// </exception>
    /// <returns>A lock handle, or <c>null</c> if the lock is currently held by another connection.</returns>
    public static async Task<IDistributedLockHandle?> TryAcquireDistributedLockAsync(
        this DatabaseFacade database,
        string key,
        CancellationToken ct = default
    )
    {
        var (ctx, provider, connection, openedByMe) = await PrepareAsync(database, key, ct).ConfigureAwait(false);
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
    /// <exception cref="LockingConfigurationException">
    /// Thrown if the key is invalid, no provider is registered, or the provider does not support distributed locks.
    /// </exception>
    /// <exception cref="LockTimeoutException">
    /// Thrown if <paramref name="timeout"/> is exceeded before the lock can be acquired.
    /// </exception>
    public static IDistributedLockHandle AcquireDistributedLock(
        this DatabaseFacade database,
        string key,
        TimeSpan? timeout = null
    )
    {
        var (ctx, provider, connection, openedByMe) = PrepareSync(database, key);
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
    /// <exception cref="LockingConfigurationException">
    /// Thrown if the key is invalid, no provider is registered, or the provider does not support distributed locks.
    /// </exception>
    /// <returns>A lock handle, or <c>null</c> if the lock is currently held by another connection.</returns>
    public static IDistributedLockHandle? TryAcquireDistributedLock(this DatabaseFacade database, string key)
    {
        var (ctx, provider, connection, openedByMe) = PrepareSync(database, key);
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

    /// <summary>Returns true if the configured EF Core provider supports distributed locks.</summary>
    public static bool SupportsDistributedLocks(this DatabaseFacade database)
    {
        var lp = ((IInfrastructure<IServiceProvider>)database).Instance.GetService<ILockingProvider>();
        return lp?.AdvisoryLockProvider is not null;
    }

    private static async Task<(
        DbContext ctx,
        IAdvisoryLockProvider provider,
        DbConnection connection,
        bool openedByMe
    )> PrepareAsync(DatabaseFacade database, string key, CancellationToken ct)
    {
        ValidateKey(key);
        var ctx = GetContext(database);
        var provider = ResolveProvider(database);
        var connection = database.GetDbConnection();
        bool openedByMe = false;
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            openedByMe = true;
        }
        return (ctx, provider, connection, openedByMe);
    }

    private static (
        DbContext ctx,
        IAdvisoryLockProvider provider,
        DbConnection connection,
        bool openedByMe
    ) PrepareSync(DatabaseFacade database, string key)
    {
        ValidateKey(key);
        var ctx = GetContext(database);
        var provider = ResolveProvider(database);
        var connection = database.GetDbConnection();
        bool openedByMe = false;
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
            openedByMe = true;
        }
        return (ctx, provider, connection, openedByMe);
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new LockingConfigurationException("Lock key must not be null or empty.");
        if (key.Length > 255)
            throw new LockingConfigurationException("Lock key must not exceed 255 characters.");
    }

    private static DbContext GetContext(DatabaseFacade database) =>
        ((IDatabaseFacadeDependenciesAccessor)database).Context;

    private static IAdvisoryLockProvider ResolveProvider(DatabaseFacade database)
    {
        var lp = ((IInfrastructure<IServiceProvider>)database).Instance.GetService<ILockingProvider>();
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
