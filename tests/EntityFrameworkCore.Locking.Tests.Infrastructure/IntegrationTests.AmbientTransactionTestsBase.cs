using System.Transactions;
using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.Tests.Infrastructure;

public abstract partial class IntegrationTestsBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RowLock_WithExplicitlyEnlistedTransaction_ExecutesSuccessfully(bool share)
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx);
        await ctx.Database.OpenConnectionAsync();
        using var transaction = new CommittableTransaction(
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted }
        );
        ctx.Database.EnlistTransaction(transaction);
        ctx.Database.CurrentTransaction.Should().BeNull();
        Transaction.Current.Should().BeNull();

        var query = ctx.Products.Where(p => p.Id == id);
        query = share ? query.ForShare() : query.ForUpdate();
        (await query.SingleAsync()).Id.Should().Be(id);
        transaction.Rollback();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RowLock_WithAmbientTransaction_ExecutesSuccessfully(bool share, bool sync)
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx);

        using (var scope = NewRowLockScope())
        {
            var query = ctx.Products.Where(p => p.Id == id);
            query = share ? query.ForShare() : query.ForUpdate();
            var product = sync ? query.Single() : await query.SingleAsync();

            product.Id.Should().Be(id);
            ctx.Database.CurrentTransaction.Should().BeNull();
            // Automatic ambient enlistment does not populate EF's explicit enlistment either.
            ctx.Database.GetEnlistedTransaction().Should().BeNull();
            scope.Complete();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForUpdate_WithAmbientTransaction_HoldsLockUntilScopeDisposal(bool complete)
    {
        await using var owner = CreateContext();
        var (_, id) = await SeedAsync(owner);
        // Keep the connection open so the test also covers enlistment after opening.
        await owner.Database.OpenConnectionAsync();
        await using var contender = CreateContext();

        async Task AssertContendedAsync()
        {
            using var suppressed = new TransactionScope(
                TransactionScopeOption.Suppress,
                TransactionScopeAsyncFlowOption.Enabled
            );
            await using var transaction = await contender.Database.BeginTransactionAsync();
            Func<Task> acquire = async () =>
                await contender.Products.Where(p => p.Id == id).ForUpdate(LockBehavior.NoWait).SingleAsync();
            await acquire.Should().ThrowAsync<LockTimeoutException>();
            await transaction.RollbackAsync();
        }

        using (var scope = NewRowLockScope())
        {
            (await owner.Products.Where(p => p.Id == id).ForUpdate().SingleAsync()).Id.Should().Be(id);
            await AssertContendedAsync();
            if (complete)
                scope.Complete();
        }

        await using var afterRelease = await contender.Database.BeginTransactionAsync();
        (await contender.Products.Where(p => p.Id == id).ForUpdate(LockBehavior.NoWait).SingleAsync())
            .Id.Should()
            .Be(id);
        await afterRelease.RollbackAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RowLock_WithSuppressedAmbientTransaction_RequiresTransaction(bool share)
    {
        await using var ctx = CreateContext();
        using var scope = NewRowLockScope();
        using var suppressed = new TransactionScope(
            TransactionScopeOption.Suppress,
            TransactionScopeAsyncFlowOption.Enabled
        );
        var query = ctx.Products.Where(p => p.Id == 1);
        query = share ? query.ForShare() : query.ForUpdate();
        Func<Task> acquire = async () => await query.SingleOrDefaultAsync();
        await acquire.Should().ThrowAsync<LockingConfigurationException>();
    }

    private static TransactionScope NewRowLockScope() =>
        new(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled
        );
}
