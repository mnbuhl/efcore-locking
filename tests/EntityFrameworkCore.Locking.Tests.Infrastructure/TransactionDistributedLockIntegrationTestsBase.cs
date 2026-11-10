using System.Data;
using System.Transactions;
using AwesomeAssertions;
using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace EntityFrameworkCore.Locking.Tests.Infrastructure;

public abstract class TransactionDistributedLockIntegrationTestsBase : IAsyncLifetime
{
    protected abstract TestDbContext CreateContext();

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Key() => $"transaction-{Guid.NewGuid():N}";

    protected static Task<IDistributedLockHandle> Acquire(
        TestDbContext context,
        string key,
        bool sync = false,
        DistributedLockMode mode = DistributedLockMode.Exclusive,
        TimeSpan? timeout = null,
        CancellationToken ct = default
    ) =>
        sync
            ? Task.FromResult(
                context.Database.AcquireDistributedLock(key, timeout, mode, scope: DistributedLockScope.Transaction)
            )
            : context.Database.AcquireDistributedLockAsync(
                key,
                timeout,
                ct,
                mode,
                scope: DistributedLockScope.Transaction
            );

    protected static async Task<IDistributedLockHandle> Try(
        TestDbContext context,
        string key,
        bool sync = false,
        DistributedLockMode mode = DistributedLockMode.Exclusive
    )
    {
        var result = sync
            ? context.Database.TryAcquireDistributedLock(key, mode, scope: DistributedLockScope.Transaction)
            : await context.Database.TryAcquireDistributedLockAsync(
                key,
                mode: mode,
                scope: DistributedLockScope.Transaction
            );
        result.Should().NotBeNull("transaction attempts always return an outcome");
        return result!;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransactionHandleReleaseAndDisposal_KeepLockUntilTransactionCompletion(bool sync)
    {
        await using var owner = CreateContext();
        await using var contender = CreateContext();
        await using var ownerTransaction = await owner.Database.BeginTransactionAsync();
        await using var contenderTransaction = await contender.Database.BeginTransactionAsync();
        var key = Key();
        var handle = await Acquire(owner, key, sync);
        handle.Key.Should().Be(key);
        handle.IsAcquired.Should().BeTrue();
        handle.Release();
        await handle.ReleaseAsync();
        handle.Dispose();
        await handle.DisposeAsync();
        (await Try(contender, key, sync)).IsAcquired.Should().BeFalse();
        await ownerTransaction.CommitAsync();
        (await Try(contender, key, sync)).IsAcquired.Should().BeTrue();
        handle.IsAcquired.Should().BeTrue("the handle records the acquisition attempt");
    }

    [Theory]
    [InlineData(false, DistributedLockMode.Exclusive)]
    [InlineData(true, DistributedLockMode.Exclusive)]
    [InlineData(false, DistributedLockMode.Shared)]
    [InlineData(true, DistributedLockMode.Shared)]
    public async Task FreeAcquisition_AndRepeatedTry_Succeed(bool sync, DistributedLockMode mode)
    {
        await using var context = CreateContext();
        context.Database.SupportsTransactionDistributedLocks().Should().BeTrue();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var key = Key();
        await Acquire(context, key, sync, mode);
        var result = await Try(context, key, sync, mode);
        result.IsAcquired.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Completion_ReleasesLock_WithoutChangingResults(bool sync, bool commit)
    {
        await using var owner = CreateContext();
        await using var contender = CreateContext();
        await using var ownerTransaction = await owner.Database.BeginTransactionAsync();
        await using var contenderTransaction = await contender.Database.BeginTransactionAsync();
        var key = Key();
        var acquired = await Try(owner, key, sync);
        acquired.IsAcquired.Should().BeTrue();
        var waiting = await Try(contender, key, sync).WaitAsync(TimeSpan.FromSeconds(5));
        waiting.IsAcquired.Should().BeFalse();
        if (commit)
            await ownerTransaction.CommitAsync();
        else
            await ownerTransaction.RollbackAsync();
        (await Try(contender, key, sync)).IsAcquired.Should().BeTrue();
        acquired.IsAcquired.Should().BeTrue("the result records the acquisition attempt");
        waiting.IsAcquired.Should().BeFalse("an earlier failed attempt is immutable too");
    }

    [Fact]
    public async Task ExplicitTransaction_DisposalWithoutCommit_ReleasesLock()
    {
        await using var owner = CreateContext();
        await using var contender = CreateContext();
        await using var contenderTransaction = await contender.Database.BeginTransactionAsync();
        var key = Key();
        IDistributedLockHandle result;
        await using (var ownerTransaction = await owner.Database.BeginTransactionAsync())
        {
            result = await Try(owner, key);
            result.IsAcquired.Should().BeTrue();
            (await Try(contender, key)).IsAcquired.Should().BeFalse();
        }
        (await Try(contender, key)).IsAcquired.Should().BeTrue();
        result.IsAcquired.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalTransaction_OwnsLockAfterEfWrapperDisposal(bool commit)
    {
        await using var owner = CreateContext();
        var connection = owner.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var external = await connection.BeginTransactionAsync();
        var wrapper =
            await owner.Database.UseTransactionAsync(external)
            ?? throw new InvalidOperationException("EF must attach the external transaction.");
        var key = Key();
        var result = await Try(owner, key);
        result.IsAcquired.Should().BeTrue();
        await wrapper.DisposeAsync();
        connection.State.Should().Be(ConnectionState.Open);
        owner.Database.CurrentTransaction.Should().BeNull();
        await Assert.ThrowsAsync<LockingConfigurationException>(() => Try(owner, key));
        await using var contender = CreateContext();
        await using var contenderTransaction = await contender.Database.BeginTransactionAsync();
        (await Try(contender, key)).IsAcquired.Should().BeFalse();
        if (commit)
            await external.CommitAsync();
        else
            await external.RollbackAsync();
        (await Try(contender, key)).IsAcquired.Should().BeTrue();
        result.IsAcquired.Should().BeTrue();
    }

    [Theory]
    [InlineData(DistributedLockMode.Shared, DistributedLockMode.Shared, true)]
    [InlineData(DistributedLockMode.Shared, DistributedLockMode.Exclusive, false)]
    [InlineData(DistributedLockMode.Exclusive, DistributedLockMode.Shared, false)]
    [InlineData(DistributedLockMode.Exclusive, DistributedLockMode.Exclusive, false)]
    public async Task SharedAndExclusiveModes_UseNativeCompatibility(
        DistributedLockMode ownerMode,
        DistributedLockMode contenderMode,
        bool compatible
    )
    {
        await using var owner = CreateContext();
        await using var contender = CreateContext();
        await using var ownerTransaction = await owner.Database.BeginTransactionAsync();
        await using var contenderTransaction = await contender.Database.BeginTransactionAsync();
        var key = Key();
        await Acquire(owner, key, mode: ownerMode);
        (await Try(contender, key, mode: contenderMode)).IsAcquired.Should().Be(compatible);
        await ownerTransaction.CommitAsync();
        (await Try(contender, key, mode: contenderMode)).IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task Reacquisition_WithStrongerMode_DoesNotSkipNativeContention()
    {
        await using var first = CreateContext();
        await using var second = CreateContext();
        await using var firstTransaction = await first.Database.BeginTransactionAsync();
        await using var secondTransaction = await second.Database.BeginTransactionAsync();
        var key = Key();
        await Acquire(first, key, mode: DistributedLockMode.Shared);
        await Acquire(second, key, mode: DistributedLockMode.Shared);
        (await Try(first, key)).IsAcquired.Should().BeFalse();
        await secondTransaction.RollbackAsync();
        (await Try(first, key)).IsAcquired.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionAndTransactionLocks_ContendOnSameResource(bool sessionOwns)
    {
        await using var session = CreateContext();
        await using var transactional = CreateContext();
        await using var transaction = await transactional.Database.BeginTransactionAsync();
        var key = Key();
        if (sessionOwns)
        {
            var handle = await session.Database.AcquireDistributedLockAsync(key);
            await using (handle)
                (await Try(transactional, key)).IsAcquired.Should().BeFalse();
            (await Try(transactional, key)).IsAcquired.Should().BeTrue();
        }
        else
        {
            await Acquire(transactional, key);
            (await session.Database.TryAcquireDistributedLockAsync(key)).Should().BeNull();
            await transaction.CommitAsync();
            await using var handle = await session.Database.TryAcquireDistributedLockAsync(key);
            handle.Should().NotBeNull();
        }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 150)]
    [InlineData(true, 150)]
    public async Task Timeout_IsTyped_AndCallerCanRollback(bool sync, int milliseconds)
    {
        await using var owner = CreateContext();
        await using var contender = CreateContext();
        await using var ownerTransaction = await owner.Database.BeginTransactionAsync();
        await using var contenderTransaction = await contender.Database.BeginTransactionAsync();
        var key = Key();
        await Acquire(owner, key);
        await Assert.ThrowsAsync<LockTimeoutException>(() =>
            Acquire(contender, key, sync, timeout: TimeSpan.FromMilliseconds(milliseconds))
        );
        await contenderTransaction.RollbackAsync();
        await ownerTransaction.CommitAsync();
        await using var retryTransaction = await contender.Database.BeginTransactionAsync();
        (await Try(contender, key)).IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task ContendedAcquisition_CancellationIsNotAFalseResult()
    {
        await using var owner = CreateContext();
        await using var contender = CreateContext();
        await using var ownerTransaction = await owner.Database.BeginTransactionAsync();
        await using var contenderTransaction = await contender.Database.BeginTransactionAsync();
        var key = Key();
        await Acquire(owner, key);
        (await Try(contender, key)).IsAcquired.Should().BeFalse();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Acquire(contender, key, timeout: TimeSpan.FromSeconds(10), ct: cancellation.Token)
                .WaitAsync(TimeSpan.FromSeconds(15))
        );
        await contenderTransaction.RollbackAsync();
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task AmbientEnlistedScope_ReleasesOnDisposal_AndPreservesOutcome(
        bool complete,
        bool acquire,
        bool sync
    )
    {
        await using var owner = CreateContext();
        await using var contender = CreateContext();
        await using var contenderTransaction = await contender.Database.BeginTransactionAsync();
        var key = Key();
        IDistributedLockHandle result;
        using (var scope = NewScope())
        {
            await owner.Database.OpenConnectionAsync();
            owner.Database.EnlistTransaction(Transaction.Current);
            owner.Database.CurrentTransaction.Should().BeNull();
            owner.Database.GetEnlistedTransaction().Should().NotBeNull();
            if (acquire)
                await Acquire(owner, key, sync, timeout: TimeSpan.FromMilliseconds(500));
            result = await Try(owner, key, sync);
            result.IsAcquired.Should().BeTrue();
            (await Try(contender, key)).IsAcquired.Should().BeFalse();
            if (complete)
                scope.Complete();
            (await Try(contender, key))
                .IsAcquired.Should()
                .BeFalse("Complete is a vote; disposal commits the owning scope");
        }
        (await Try(contender, key)).IsAcquired.Should().BeTrue();
        result.IsAcquired.Should().BeTrue();
        owner.Database.GetDbConnection().State.Should().Be(ConnectionState.Open);
    }

    [Fact]
    public async Task AmbientPresence_WithoutExplicitEfEnlistment_IsRejected()
    {
        await using var context = CreateContext();
        using var scope = NewScope();
        await context.Database.GetDbConnection().OpenAsync();
        context.Database.GetEnlistedTransaction().Should().BeNull();
        await Assert.ThrowsAsync<LockingConfigurationException>(() => Try(context, Key()));
        context.Database.GetDbConnection().State.Should().Be(ConnectionState.Open);
    }

    [Fact]
    public async Task AmbientPresence_DoesNotOpenClosedConnection()
    {
        await using var context = CreateContext();
        using var scope = NewScope();
        await Assert.ThrowsAsync<LockingConfigurationException>(() => Acquire(context, Key()));
        context.Database.GetDbConnection().State.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public async Task EnlistedConnection_DoesNotAcquireInSuppressedOrDifferentScope()
    {
        await using var context = CreateContext();
        using var ownerScope = NewScope();
        await context.Database.OpenConnectionAsync();
        context.Database.EnlistTransaction(Transaction.Current);
        using (
            var suppressed = new TransactionScope(
                TransactionScopeOption.Suppress,
                TransactionScopeAsyncFlowOption.Enabled
            )
        )
            await Assert.ThrowsAsync<LockingConfigurationException>(() => Acquire(context, Key()));
        using (var different = NewScope())
            await Assert.ThrowsAsync<LockingConfigurationException>(() => Acquire(context, Key()));
    }

    [Fact]
    public async Task CompletedAmbientScope_IsRejectedWithoutClosingConnection()
    {
        await using var context = CreateContext();
        using (var scope = NewScope())
        {
            await context.Database.OpenConnectionAsync();
            context.Database.EnlistTransaction(Transaction.Current);
            scope.Complete();
        }
        await Assert.ThrowsAsync<LockingConfigurationException>(() => Acquire(context, Key()));
        context.Database.GetDbConnection().State.Should().Be(ConnectionState.Open);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadCheckWrite_SeesOnlyTheCommittedReservationAfterAcquisition(bool commit)
    {
        int productId;
        await using (var seed = CreateContext())
        {
            var product = new Product
            {
                Name = Key(),
                Stock = 1,
                Price = 1,
                Category = new Category { Name = "transaction lock test" },
            };
            seed.Products.Add(product);
            await seed.SaveChangesAsync();
            productId = product.Id;
        }
        var key = $"stock:{productId}";
        await using var owner = CreateContext();
        await using var contender = CreateContext();
        await using var ownerTransaction = await owner.Database.BeginTransactionAsync();
        await using var contenderTransaction = await contender.Database.BeginTransactionAsync();
        await Acquire(owner, key);
        (await owner.OrderLines.Where(line => line.ProductId == productId).SumAsync(line => line.Quantity))
            .Should()
            .Be(0);
        owner.OrderLines.Add(
            new OrderLine
            {
                ProductId = productId,
                Quantity = 1,
                UnitPrice = 1,
            }
        );
        await owner.SaveChangesAsync();
        (await Try(contender, key)).IsAcquired.Should().BeFalse();
        if (commit)
            await ownerTransaction.CommitAsync();
        else
            await ownerTransaction.RollbackAsync();
        (await Try(contender, key)).IsAcquired.Should().BeTrue();
        var reserved = await contender
            .OrderLines.Where(line => line.ProductId == productId)
            .SumAsync(line => line.Quantity);
        reserved.Should().Be(commit ? 1 : 0);
        if (reserved == 0)
        {
            contender.OrderLines.Add(
                new OrderLine
                {
                    ProductId = productId,
                    Quantity = 1,
                    UnitPrice = 1,
                }
            );
            await contender.SaveChangesAsync();
        }
        await contenderTransaction.CommitAsync();
        await using var verify = CreateContext();
        (await verify.OrderLines.Where(line => line.ProductId == productId).SumAsync(line => line.Quantity))
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task NativeDeadlock_IsTyped_AndCallerRollbackUnblocksSurvivor()
    {
        await using var first = CreateContext();
        await using var second = CreateContext();
        await using var firstTransaction = await first.Database.BeginTransactionAsync();
        await using var secondTransaction = await second.Database.BeginTransactionAsync();
        var firstKey = Key();
        var secondKey = Key();
        await Acquire(first, firstKey);
        await Acquire(second, secondKey);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var firstWait = Acquire(first, secondKey, ct: cancellation.Token);
        var secondWait = Acquire(second, firstKey, ct: cancellation.Token);
        try
        {
            var victim = await Task.WhenAny(firstWait, secondWait).WaitAsync(TimeSpan.FromSeconds(25));
            var failure = await Record.ExceptionAsync(() => victim);
            if (failure is null)
            {
                // PostgreSQL aborts its victim and releases locks before its error necessarily reaches the caller.
                victim = ReferenceEquals(victim, firstWait) ? secondWait : firstWait;
                failure = await Record.ExceptionAsync(() => victim.WaitAsync(TimeSpan.FromSeconds(5)));
            }
            failure.Should().BeOfType<DeadlockException>();
            if (ReferenceEquals(victim, firstWait))
                await firstTransaction.RollbackAsync();
            else
                await secondTransaction.RollbackAsync();
            var survivor = ReferenceEquals(victim, firstWait) ? secondWait : firstWait;
            await survivor.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                await Task.WhenAll(firstWait, secondWait).WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            { /* Observe both wait tasks; the assertion above owns the outcome. */
            }
        }
    }

    private static TransactionScope NewScope() =>
        new(
            TransactionScopeOption.RequiresNew,
            new TransactionOptions { IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled
        );
}
