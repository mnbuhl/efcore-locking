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
    /// <remarks>
    /// Transaction scope uses the caller's existing transaction and never opens/closes the connection or creates/enlists a transaction.
    /// The transaction owns the lock lifetime; releasing or disposing its result is a no-op.
    /// IsAcquired records the acquisition outcome and does not track transaction completion or native savepoint rollback.
    /// After a transaction acquisition failure, roll back the transaction before continuing.
    /// </remarks>
    /// <param name="database">The <see cref="DatabaseFacade"/> whose connection will hold the lock.</param>
    /// <param name="key">Lock key (1–255 characters).</param>
    /// <param name="timeout">Maximum time to wait. Throws <see cref="LockTimeoutException"/> if exceeded. Null = wait indefinitely.</param>
    /// <param name="ct">Cancellation token. Cancellation is best-effort (driver-dependent).</param>
    /// <param name="mode">Distributed lock mode. Defaults to exclusive.</param>
    /// <param name="scope">Session by default; Transaction requires an open connection and an active EF transaction or explicitly EF-enlisted ambient transaction.</param>
    /// <exception cref="LockingConfigurationException">
    /// Thrown if the key is null, empty, or longer than 255 characters; if no locking provider is
    /// registered; if the provider does not support the requested scope; or if transaction setup or scope is invalid.
    /// </exception>
    /// <exception cref="LockTimeoutException">
    /// Thrown if <paramref name="timeout"/> is exceeded before the lock can be acquired.
    /// </exception>
    public static async Task<IDistributedLockHandle> AcquireDistributedLockAsync(
        this DatabaseFacade database,
        string key,
        TimeSpan? timeout = null,
        CancellationToken ct = default,
        DistributedLockMode mode = DistributedLockMode.Exclusive,
        DistributedLockScope scope = DistributedLockScope.Session
    )
    {
        ValidateScope(scope);
        if (scope == DistributedLockScope.Transaction)
        {
            ct.ThrowIfCancellationRequested();
            var (transactionContext, transactionProvider, transactionConnection) = PrepareTransaction(
                database,
                key,
                mode,
                timeout
            );
            return await transactionProvider
                .AcquireAsync(transactionContext, transactionConnection, key, timeout, ct, mode, scope)
                .ConfigureAwait(false);
        }
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
    /// For Session, returns null immediately on contention. For Transaction, returns a non-null result with IsAcquired=false.
    /// </summary>
    /// <remarks>
    /// Transaction scope uses the caller's existing transaction and never opens/closes the connection or creates/enlists a transaction.
    /// The transaction owns the lock lifetime; releasing or disposing its result is a no-op.
    /// IsAcquired records the acquisition outcome and does not track transaction completion or native savepoint rollback.
    /// After a transaction acquisition failure, roll back the transaction before continuing.
    /// </remarks>
    /// <param name="database">The <see cref="DatabaseFacade"/> whose connection will hold the lock.</param>
    /// <param name="key">Lock key (1–255 characters).</param>
    /// <param name="ct">Cancellation token. Cancellation is best-effort (driver-dependent).</param>
    /// <param name="mode">Distributed lock mode. Defaults to exclusive.</param>
    /// <param name="scope">Session by default; Transaction requires an open connection and an active EF transaction or explicitly EF-enlisted ambient transaction.</param>
    /// <exception cref="LockingConfigurationException">
    /// Thrown if the key is null, empty, or longer than 255 characters; if no locking provider is
    /// registered; if the provider does not support the requested scope; or if transaction setup or scope is invalid.
    /// </exception>
    /// <returns>A session handle or null on contention; for Transaction, an immutable non-null IsAcquired result whose release/disposal are no-ops.</returns>
    public static async Task<IDistributedLockHandle?> TryAcquireDistributedLockAsync(
        this DatabaseFacade database,
        string key,
        CancellationToken ct = default,
        DistributedLockMode mode = DistributedLockMode.Exclusive,
        DistributedLockScope scope = DistributedLockScope.Session
    )
    {
        ValidateScope(scope);
        if (scope == DistributedLockScope.Transaction)
        {
            ct.ThrowIfCancellationRequested();
            var (transactionContext, transactionProvider, transactionConnection) = PrepareTransaction(
                database,
                key,
                mode
            );
            return await transactionProvider
                .TryAcquireAsync(transactionContext, transactionConnection, key, ct, mode, scope)
                .ConfigureAwait(false);
        }
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
    /// <remarks>
    /// Transaction scope uses the caller's existing transaction and never opens/closes the connection or creates/enlists a transaction.
    /// The transaction owns the lock lifetime; releasing or disposing its result is a no-op.
    /// IsAcquired records the acquisition outcome and does not track transaction completion or native savepoint rollback.
    /// After a transaction acquisition failure, roll back the transaction before continuing.
    /// </remarks>
    /// <param name="database">The <see cref="DatabaseFacade"/> whose connection will hold the lock.</param>
    /// <param name="key">Lock key (1–255 characters).</param>
    /// <param name="timeout">Maximum time to wait. Null = wait indefinitely.</param>
    /// <param name="mode">Distributed lock mode. Defaults to exclusive.</param>
    /// <param name="scope">Session by default; Transaction requires an open connection and an active EF transaction or explicitly EF-enlisted ambient transaction.</param>
    /// <exception cref="LockingConfigurationException">
    /// Thrown if the key, scope, or transaction setup is invalid, no provider is registered, or the requested scope is unsupported.
    /// </exception>
    /// <exception cref="LockTimeoutException">
    /// Thrown if <paramref name="timeout"/> is exceeded before the lock can be acquired.
    /// </exception>
    public static IDistributedLockHandle AcquireDistributedLock(
        this DatabaseFacade database,
        string key,
        TimeSpan? timeout = null,
        DistributedLockMode mode = DistributedLockMode.Exclusive,
        DistributedLockScope scope = DistributedLockScope.Session
    )
    {
        ValidateScope(scope);
        if (scope == DistributedLockScope.Transaction)
        {
            var (transactionContext, transactionProvider, transactionConnection) = PrepareTransaction(
                database,
                key,
                mode,
                timeout
            );
            return transactionProvider.Acquire(transactionContext, transactionConnection, key, timeout, mode, scope);
        }
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

    /// <summary>Attempts a distributed lock synchronously; Session returns null on contention, Transaction returns IsAcquired=false.</summary>
    /// <remarks>
    /// Transaction scope uses the caller's existing transaction and never opens/closes the connection or creates/enlists a transaction.
    /// The transaction owns the lock lifetime; releasing or disposing its result is a no-op.
    /// IsAcquired records the acquisition outcome and does not track transaction completion or native savepoint rollback.
    /// After a transaction acquisition failure, roll back the transaction before continuing.
    /// </remarks>
    /// <param name="database">The <see cref="DatabaseFacade"/> whose connection will hold the lock.</param>
    /// <param name="key">Lock key (1–255 characters).</param>
    /// <param name="mode">Distributed lock mode. Defaults to exclusive.</param>
    /// <param name="scope">Session by default; Transaction requires an open connection and an active EF transaction or explicitly EF-enlisted ambient transaction.</param>
    /// <exception cref="LockingConfigurationException">
    /// Thrown if the key, scope, or transaction setup is invalid, no provider is registered, or the requested scope is unsupported.
    /// </exception>
    /// <returns>A session handle or null on contention; for Transaction, an immutable non-null IsAcquired result whose release/disposal are no-ops.</returns>
    public static IDistributedLockHandle? TryAcquireDistributedLock(
        this DatabaseFacade database,
        string key,
        DistributedLockMode mode = DistributedLockMode.Exclusive,
        DistributedLockScope scope = DistributedLockScope.Session
    )
    {
        ValidateScope(scope);
        if (scope == DistributedLockScope.Transaction)
        {
            var (transactionContext, transactionProvider, transactionConnection) = PrepareTransaction(
                database,
                key,
                mode
            );
            return transactionProvider.TryAcquire(transactionContext, transactionConnection, key, mode, scope);
        }
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

    /// <summary>Acquires a session-scoped lock using the original overload.</summary>
    public static Task<IDistributedLockHandle> AcquireDistributedLockAsync(
        this DatabaseFacade database,
        string key,
        TimeSpan? timeout,
        CancellationToken ct,
        DistributedLockMode mode
    ) => AcquireDistributedLockAsync(database, key, timeout, ct, mode, DistributedLockScope.Session);

    /// <summary>Attempts a session-scoped lock using the original overload.</summary>
    public static Task<IDistributedLockHandle?> TryAcquireDistributedLockAsync(
        this DatabaseFacade database,
        string key,
        CancellationToken ct,
        DistributedLockMode mode
    ) => TryAcquireDistributedLockAsync(database, key, ct, mode, DistributedLockScope.Session);

    /// <summary>Acquires a session-scoped lock using the original overload.</summary>
    public static IDistributedLockHandle AcquireDistributedLock(
        this DatabaseFacade database,
        string key,
        TimeSpan? timeout,
        DistributedLockMode mode
    ) => AcquireDistributedLock(database, key, timeout, mode, DistributedLockScope.Session);

    /// <summary>Attempts a session-scoped lock using the original overload.</summary>
    public static IDistributedLockHandle? TryAcquireDistributedLock(
        this DatabaseFacade database,
        string key,
        DistributedLockMode mode
    ) => TryAcquireDistributedLock(database, key, mode, DistributedLockScope.Session);

    /// <summary>Returns true if the configured EF Core provider supports distributed locks.</summary>
    public static bool SupportsDistributedLocks(this DatabaseFacade database)
    {
        var lp = ((IInfrastructure<IServiceProvider>)database).Instance.GetService<ILockingProvider>();
        return lp?.AdvisoryLockProvider is not null;
    }

    /// <summary>Whether the configured provider supports native transaction-scoped distributed locks.</summary>
    /// <remarks>This reports capability only, not whether the connection or transaction is ready.</remarks>
    public static bool SupportsTransactionDistributedLocks(this DatabaseFacade database)
    {
        var lp = ((IInfrastructure<IServiceProvider>)database).Instance.GetService<ILockingProvider>();
        return lp?.AdvisoryLockProvider?.SupportsTransactionLocks ?? false;
    }

    private static (DbContext ctx, IAdvisoryLockProvider provider, DbConnection connection) PrepareTransaction(
        DatabaseFacade database,
        string key,
        DistributedLockMode mode,
        TimeSpan? timeout = null
    )
    {
        ValidateKey(key);
        _ = TransactionDistributedLockTimeout.ToMilliseconds(timeout);
        var lp = ((IInfrastructure<IServiceProvider>)database).Instance.GetService<ILockingProvider>();
        if (lp is null)
            throw new LockingConfigurationException(
                "No ILockingProvider is registered. Call UseLocking() when configuring the DbContext."
            );
        var provider = lp.AdvisoryLockProvider;
        if (provider is null || !provider.SupportsTransactionLocks)
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

    private static void ValidateScope(DistributedLockScope scope)
    {
        if (scope is not DistributedLockScope.Session and not DistributedLockScope.Transaction)
            throw new LockingConfigurationException($"Unsupported distributed lock scope: {scope}.");
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
