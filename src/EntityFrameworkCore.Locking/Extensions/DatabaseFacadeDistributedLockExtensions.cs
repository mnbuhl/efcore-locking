using System.Data;
using System.Data.Common;
using System.Transactions;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable once CheckNamespace
namespace EntityFrameworkCore.Locking;

/// <summary>
/// Extension methods on <see cref="DatabaseFacade"/> (accessed via <c>DbContext.Database</c>) for
/// acquiring session-scoped or transaction-scoped distributed (advisory) locks.
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
    /// <param name="mode">Distributed lock mode. Defaults to exclusive.</param>
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
        CancellationToken ct = default,
        DistributedLockMode mode = DistributedLockMode.Exclusive
    )
    {
        var (ctx, provider, connection, openedByMe) = await PrepareAsync(database, key, ct, mode).ConfigureAwait(false);
        try
        {
            DistributedLockRegistry.RegisterOrThrow(ctx, connection, key, mode);
            try
            {
                return await provider.AcquireAsync(ctx, connection, key, timeout, ct, mode).ConfigureAwait(false);
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
    /// <param name="database">The <see cref="DatabaseFacade"/> whose connection will hold the lock.</param>
    /// <param name="key">Lock key (1–255 characters).</param>
    /// <param name="ct">Cancellation token. Cancellation is best-effort (driver-dependent).</param>
    /// <param name="mode">Distributed lock mode. Defaults to exclusive.</param>
    /// <exception cref="LockingConfigurationException">
    /// Thrown if the key is null, empty, or longer than 255 characters; if no locking provider is
    /// registered; or if the provider does not support distributed locks.
    /// </exception>
    /// <returns>A lock handle, or <c>null</c> if the lock is currently held by another connection.</returns>
    public static async Task<IDistributedLockHandle?> TryAcquireDistributedLockAsync(
        this DatabaseFacade database,
        string key,
        CancellationToken ct = default,
        DistributedLockMode mode = DistributedLockMode.Exclusive
    )
    {
        var (ctx, provider, connection, openedByMe) = await PrepareAsync(database, key, ct, mode).ConfigureAwait(false);
        try
        {
            DistributedLockRegistry.RegisterOrThrow(ctx, connection, key, mode);
            IDistributedLockHandle? handle;
            try
            {
                handle = await provider.TryAcquireAsync(ctx, connection, key, ct, mode).ConfigureAwait(false);
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
    /// <param name="database">The <see cref="DatabaseFacade"/> whose connection will hold the lock.</param>
    /// <param name="key">Lock key (1–255 characters).</param>
    /// <param name="timeout">Maximum time to wait. Null = wait indefinitely.</param>
    /// <param name="mode">Distributed lock mode. Defaults to exclusive.</param>
    /// <exception cref="LockingConfigurationException">
    /// Thrown if the key is invalid, no provider is registered, or the provider does not support distributed locks.
    /// </exception>
    /// <exception cref="LockTimeoutException">
    /// Thrown if <paramref name="timeout"/> is exceeded before the lock can be acquired.
    /// </exception>
    public static IDistributedLockHandle AcquireDistributedLock(
        this DatabaseFacade database,
        string key,
        TimeSpan? timeout = null,
        DistributedLockMode mode = DistributedLockMode.Exclusive
    )
    {
        var (ctx, provider, connection, openedByMe) = PrepareSync(database, key, mode);
        try
        {
            DistributedLockRegistry.RegisterOrThrow(ctx, connection, key, mode);
            try
            {
                return provider.Acquire(ctx, connection, key, timeout, mode);
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
    /// <param name="database">The <see cref="DatabaseFacade"/> whose connection will hold the lock.</param>
    /// <param name="key">Lock key (1–255 characters).</param>
    /// <param name="mode">Distributed lock mode. Defaults to exclusive.</param>
    /// <exception cref="LockingConfigurationException">
    /// Thrown if the key is invalid, no provider is registered, or the provider does not support distributed locks.
    /// </exception>
    /// <returns>A lock handle, or <c>null</c> if the lock is currently held by another connection.</returns>
    public static IDistributedLockHandle? TryAcquireDistributedLock(
        this DatabaseFacade database,
        string key,
        DistributedLockMode mode = DistributedLockMode.Exclusive
    )
    {
        var (ctx, provider, connection, openedByMe) = PrepareSync(database, key, mode);
        try
        {
            DistributedLockRegistry.RegisterOrThrow(ctx, connection, key, mode);
            IDistributedLockHandle? handle;
            try
            {
                handle = provider.TryAcquire(ctx, connection, key, mode);
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

    /// <summary>Acquires a distributed lock owned by the caller's existing transaction.</summary>
    /// <remarks>
    /// Requires an open connection and an active EF transaction or explicitly EF-enlisted ambient transaction.
    /// The database releases the lock on transaction completion, subject to native savepoint semantics.
    /// No handle is returned and no connection or transaction lifecycle is managed by this method.
    /// After any acquisition failure, roll back the transaction before continuing.
    /// </remarks>
    /// <param name="database">The database whose transaction will own the lock.</param>
    /// <param name="key">Lock key (1–255 characters).</param>
    /// <param name="timeout">Null for no library timeout; zero for no wait; a positive maximum wait otherwise.</param>
    /// <param name="ct">Cancellation token; driver cancellation is best-effort.</param>
    /// <param name="mode">Requested lock mode; defaults to exclusive.</param>
    /// <exception cref="LockingConfigurationException">Invalid key, mode, timeout, provider or transaction setup.</exception>
    /// <exception cref="LockTimeoutException">The lock could not be acquired within the wait budget.</exception>
    /// <exception cref="DeadlockException">The database detected an acquisition deadlock.</exception>
    public static async Task AcquireTransactionDistributedLockAsync(
        this DatabaseFacade database,
        string key,
        TimeSpan? timeout = null,
        CancellationToken ct = default,
        DistributedLockMode mode = DistributedLockMode.Exclusive
    )
    {
        ct.ThrowIfCancellationRequested();
        var (ctx, provider, connection) = PrepareTransaction(database, key, mode, timeout);
        await provider.AcquireAsync(ctx, connection, key, timeout, ct, mode).ConfigureAwait(false);
    }

    /// <summary>Attempts a transaction-owned distributed lock without waiting.</summary>
    /// <remarks>
    /// Requires the same caller-owned setup as <see cref="AcquireTransactionDistributedLockAsync"/>.
    /// The result records successful acquisition, not live ownership, and cannot release the lock.
    /// Ordinary contention returns IsAcquired=false; other failures and cancellation are exceptions.
    /// </remarks>
    /// <param name="database">The database whose transaction will own the lock.</param>
    /// <param name="key">Lock key (1–255 characters).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="mode">Requested lock mode; defaults to exclusive.</param>
    /// <returns>An immutable acquisition outcome; never null.</returns>
    /// <exception cref="LockingConfigurationException">Invalid key, mode, provider or transaction setup.</exception>
    public static async Task<TransactionDistributedLockResult> TryAcquireTransactionDistributedLockAsync(
        this DatabaseFacade database,
        string key,
        CancellationToken ct = default,
        DistributedLockMode mode = DistributedLockMode.Exclusive
    )
    {
        ct.ThrowIfCancellationRequested();
        var (ctx, provider, connection) = PrepareTransaction(database, key, mode);
        var acquired = await provider.TryAcquireAsync(ctx, connection, key, ct, mode).ConfigureAwait(false);
        return new TransactionDistributedLockResult(acquired);
    }

    /// <summary>Acquires a distributed lock synchronously in the caller's existing transaction.</summary>
    /// <remarks>Uses the setup, timeout and lifetime contract of <see cref="AcquireTransactionDistributedLockAsync"/>.</remarks>
    /// <param name="database">The database whose transaction will own the lock.</param>
    /// <param name="key">Lock key (1–255 characters).</param>
    /// <param name="timeout">Null for no library timeout; zero for no wait; a positive maximum wait otherwise.</param>
    /// <param name="mode">Requested lock mode; defaults to exclusive.</param>
    /// <exception cref="LockingConfigurationException">Invalid key, mode, timeout, provider or transaction setup.</exception>
    /// <exception cref="LockTimeoutException">The lock could not be acquired within the wait budget.</exception>
    public static void AcquireTransactionDistributedLock(
        this DatabaseFacade database,
        string key,
        TimeSpan? timeout = null,
        DistributedLockMode mode = DistributedLockMode.Exclusive
    )
    {
        var (ctx, provider, connection) = PrepareTransaction(database, key, mode, timeout);
        provider.Acquire(ctx, connection, key, timeout, mode);
    }

    /// <summary>Attempts a transaction-owned distributed lock synchronously without waiting.</summary>
    /// <remarks>Uses the same immutable outcome contract as <see cref="TryAcquireTransactionDistributedLockAsync"/>.</remarks>
    /// <param name="database">The database whose transaction will own the lock.</param>
    /// <param name="key">Lock key (1–255 characters).</param>
    /// <param name="mode">Requested lock mode; defaults to exclusive.</param>
    /// <returns>An immutable acquisition outcome; never null.</returns>
    /// <exception cref="LockingConfigurationException">Invalid key, mode, provider or transaction setup.</exception>
    public static TransactionDistributedLockResult TryAcquireTransactionDistributedLock(
        this DatabaseFacade database,
        string key,
        DistributedLockMode mode = DistributedLockMode.Exclusive
    )
    {
        var (ctx, provider, connection) = PrepareTransaction(database, key, mode);
        return new TransactionDistributedLockResult(provider.TryAcquire(ctx, connection, key, mode));
    }

    /// <summary>Whether the configured provider supports native transaction-scoped distributed locks.</summary>
    /// <remarks>This reports capability only, not whether the connection or transaction is ready.</remarks>
    public static bool SupportsTransactionDistributedLocks(this DatabaseFacade database)
    {
        var lp = ((IInfrastructure<IServiceProvider>)database).Instance.GetService<ILockingProvider>();
        return lp?.TransactionAdvisoryLockProvider is not null;
    }

    private static (
        DbContext ctx,
        ITransactionAdvisoryLockProvider provider,
        DbConnection connection
    ) PrepareTransaction(DatabaseFacade database, string key, DistributedLockMode mode, TimeSpan? timeout = null)
    {
        ValidateKey(key);
        _ = TransactionDistributedLockTimeout.ToMilliseconds(timeout);
        var lp = ((IInfrastructure<IServiceProvider>)database).Instance.GetService<ILockingProvider>();
        if (lp is null)
            throw new LockingConfigurationException(
                "No ILockingProvider is registered. Call UseLocking() when configuring the DbContext."
            );
        var provider = lp.TransactionAdvisoryLockProvider;
        if (provider is null)
            throw new LockingConfigurationException(
                $"The '{lp.ProviderName}' provider does not support transaction distributed locks."
            );
        provider.ValidateMode(mode);
        var connection = database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            throw new LockingConfigurationException(
                "Transaction distributed locks require an already-open connection. The caller must open and enlist it first."
            );

        try
        {
            if (database.CurrentTransaction is { } transaction)
            {
                if (transaction.GetDbTransaction().Connection != connection)
                    throw new LockingConfigurationException(
                        "The EF transaction must be active and belong to this connection."
                    );
            }
            else
            {
                var ambient = System.Transactions.Transaction.Current;
                var enlisted = database.GetEnlistedTransaction();
                if (
                    ambient is null
                    || enlisted is null
                    || !ambient.Equals(enlisted)
                    || ambient.TransactionInformation.Status != System.Transactions.TransactionStatus.Active
                    || enlisted.TransactionInformation.Status != System.Transactions.TransactionStatus.Active
                )
                    throw new LockingConfigurationException(
                        "Transaction distributed locks require an active EF transaction or a matching ambient transaction explicitly enlisted through Database.EnlistTransaction(Transaction.Current)."
                    );
            }
        }
        catch (ObjectDisposedException ex)
        {
            throw new LockingConfigurationException(
                "Transaction distributed locks require an active, non-disposed transaction.",
                ex
            );
        }
        catch (InvalidOperationException ex)
        {
            throw new LockingConfigurationException(
                "The transaction is completed or its active state cannot be inspected.",
                ex
            );
        }
        catch (NotSupportedException ex)
        {
            throw new LockingConfigurationException(
                "The transaction manager cannot expose the required enlistment state.",
                ex
            );
        }

        return (GetContext(database), provider, connection);
    }

    private static async Task<(
        DbContext ctx,
        IAdvisoryLockProvider provider,
        DbConnection connection,
        bool openedByMe
    )> PrepareAsync(DatabaseFacade database, string key, CancellationToken ct, DistributedLockMode mode)
    {
        ValidateKey(key);
        var ctx = GetContext(database);
        var provider = ResolveProvider(database);
        provider.ValidateMode(mode);
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
    ) PrepareSync(DatabaseFacade database, string key, DistributedLockMode mode)
    {
        ValidateKey(key);
        var ctx = GetContext(database);
        var provider = ResolveProvider(database);
        provider.ValidateMode(mode);
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
