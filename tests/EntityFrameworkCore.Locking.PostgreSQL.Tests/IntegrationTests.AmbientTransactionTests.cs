using System.Transactions;
using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace EntityFrameworkCore.Locking.PostgreSQL.Tests;

public partial class IntegrationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RowLock_WithAmbientEnlistmentDisabled_RequiresTransaction(bool share, bool sync)
    {
        await using var ctx = CreateContextWithoutAmbientEnlistment();
        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        Transaction.Current.Should().NotBeNull();

        var query = ctx.Products.Where(p => p.Id == 1);
        query = share ? query.ForShare() : query.ForUpdate();
        Func<Task> acquire = async () =>
        {
            if (sync)
                query.SingleOrDefault();
            else
                await query.SingleOrDefaultAsync();
        };
        await acquire.Should().ThrowAsync<LockingConfigurationException>();
        ctx.Database.CurrentTransaction.Should().BeNull();
        ctx.Database.GetEnlistedTransaction().Should().BeNull();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RowLock_WithAmbientEnlistmentDisabled_AcceptsExplicitTransaction(bool share, bool enlist)
    {
        await using var ctx = CreateContextWithoutAmbientEnlistment();
        var (_, id) = await SeedAsync(ctx);
        await ctx.Database.OpenConnectionAsync();
        using var enlisted = enlist ? new CommittableTransaction() : null;
        await using var efTransaction = enlist ? null : await ctx.Database.BeginTransactionAsync();
        if (enlist)
            ctx.Database.EnlistTransaction(enlisted);

        var query = ctx.Products.Where(p => p.Id == id);
        query = share ? query.ForShare() : query.ForUpdate();
        (await query.SingleAsync()).Id.Should().Be(id);
        enlisted?.Rollback();
        if (efTransaction is not null)
            await efTransaction.RollbackAsync();
    }

    private TestDbContext CreateContextWithoutAmbientEnlistment() =>
        new(
            new DbContextOptionsBuilder<TestDbContext>()
                .UseNpgsql(
                    new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Enlist = false }.ConnectionString
                )
                .UseLocking()
                .Options
        );
}
