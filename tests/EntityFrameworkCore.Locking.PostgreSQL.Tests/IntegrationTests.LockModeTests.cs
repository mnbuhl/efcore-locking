using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.PostgreSQL;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.PostgreSQL.Tests;

public partial class IntegrationTests
{
    // --- ForShare ---

    [Fact]
    public async Task ForShare_WithTransaction_ReturnsData()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx, productName: "Share Me");

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var product = await ctx.Products.Where(p => p.Id == id).ForShare().FirstOrDefaultAsync();

        product.Should().NotBeNull();
        product!.Name.Should().Be("Share Me");
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
    public async Task ForShare_NoWait_ThrowsOnForUpdateLockedRow()
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

    // --- ForNoKeyUpdate / ForKeyShare ---

    [Fact]
    public async Task ForNoKeyUpdate_WithTransaction_ExecutesSuccessfully()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx);

        await using var tx = await ctx.Database.BeginTransactionAsync();
        (await ctx.Products.Where(p => p.Id == id).ForNoKeyUpdate().FirstOrDefaultAsync()).Should().NotBeNull();
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForKeyShare_WithTransaction_ExecutesSuccessfully()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx);

        await using var tx = await ctx.Database.BeginTransactionAsync();
        (await ctx.Products.Where(p => p.Id == id).ForKeyShare().FirstOrDefaultAsync()).Should().NotBeNull();
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForKeyShare_TwoConcurrentReaders_BothSucceed()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx);

        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        (await ctxA.Products.Where(p => p.Id == id).ForKeyShare().FirstOrDefaultAsync()).Should().NotBeNull();

        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();
        (await ctxB.Products.Where(p => p.Id == id).ForKeyShare().FirstOrDefaultAsync()).Should().NotBeNull();

        await txA.RollbackAsync();
        await txB.RollbackAsync();
    }

    [Fact]
    public async Task ForNoKeyUpdate_AllowsForKeyShare_ConcurrentReaders()
    {
        // FOR NO KEY UPDATE does not block FOR KEY SHARE — both should succeed concurrently
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx);

        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        (await ctxA.Products.Where(p => p.Id == id).ForNoKeyUpdate().FirstOrDefaultAsync()).Should().NotBeNull();

        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();
        (await ctxB.Products.Where(p => p.Id == id).ForKeyShare().FirstOrDefaultAsync()).Should().NotBeNull();

        await txA.RollbackAsync();
        await txB.RollbackAsync();
    }

    [Fact]
    public async Task ForNoKeyUpdate_NoWait_ThrowsOnForUpdateLockedRow()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx);

        await using var ctxA = CreateContext();
        await using var txA = await ctxA.Database.BeginTransactionAsync();
        await ctxA.Products.Where(p => p.Id == id).ForUpdate().FirstOrDefaultAsync();

        await using var ctxB = CreateContext();
        await using var txB = await ctxB.Database.BeginTransactionAsync();
        Func<Task> act = async () =>
            await ctxB.Products.Where(p => p.Id == id).ForNoKeyUpdate(LockBehavior.NoWait).FirstOrDefaultAsync();

        await act.Should().ThrowAsync<LockTimeoutException>();
        await txA.RollbackAsync();
        await txB.RollbackAsync();
    }

    // --- Timeout ---

    [Fact]
    public async Task ForUpdate_WithTimeout_SucceedsOnUncontendedRow()
    {
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx, productName: "Uncontended");

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var row = await ctx
            .Products.Where(p => p.Id == id)
            .ForUpdate(LockBehavior.Wait, TimeSpan.FromMilliseconds(500))
            .FirstOrDefaultAsync();

        row.Should().NotBeNull();
        await tx.RollbackAsync();
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
}
