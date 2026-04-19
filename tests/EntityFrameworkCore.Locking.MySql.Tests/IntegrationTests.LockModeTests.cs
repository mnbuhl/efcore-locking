using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.MySql.Tests;

public partial class IntegrationTests
{
    [Fact]
    public async Task ForShare_WithTransaction_ReturnsData()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx, productName: "Share Me");

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var row = await ctx.Products.Where(p => p.Id == id).ForShare().FirstOrDefaultAsync();

        row.Should().NotBeNull();
        row!.Name.Should().Be("Share Me");
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForShare_TwoConcurrentReaders_BothSucceed()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx, productName: "Shared");

        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        (await ctxA.Products.Where(p => p.Id == id).ForShare().FirstOrDefaultAsync()).Should().NotBeNull();

        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();
        (await ctxB.Products.Where(p => p.Id == id).ForShare().FirstOrDefaultAsync()).Should().NotBeNull();

        await txA.RollbackAsync();
        await txB.RollbackAsync();
    }

    [Fact]
    public async Task ForShare_NoWait_ThrowsOnLockedRow()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx, productName: "ShareNoWait");

        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        await ctxA.Products.Where(p => p.Id == id).ForUpdate().FirstOrDefaultAsync();

        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();
        Func<Task> act = async () =>
            await ctxB.Products.Where(p => p.Id == id).ForShare(LockBehavior.NoWait).FirstOrDefaultAsync();

        await act.Should().ThrowAsync<LockTimeoutException>();
        await txA.RollbackAsync();
        await txB.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_NoWait_OnUncontendedRow_Succeeds()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx, productName: "NoWaitFree");

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var row = await ctx.Products.Where(p => p.Id == id).ForUpdate(LockBehavior.NoWait).FirstOrDefaultAsync();

        row.Should().NotBeNull();
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_WithTimeout_SucceedsOnUncontendedRow()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx, productName: "Uncontended");

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var row = await ctx
            .Products.Where(p => p.Id == id)
            .ForUpdate(LockBehavior.Wait, TimeSpan.FromSeconds(1))
            .FirstOrDefaultAsync();

        row.Should().NotBeNull();
        await tx.RollbackAsync();
    }
}
