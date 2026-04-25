using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.Tests.Infrastructure;

public abstract partial class IntegrationTestsBase
{
    [Fact]
    public async Task ForUpdate_WhenFollowedByPlainQuery_ShouldNotEmitLockOnSecondQuery()
    {
        var (ctx, cap) = CreateContextWithCapture();
        await using (ctx)
        {
            var (_, id) = await SeedAsync(ctx);

            await using var tx = await ctx.Database.BeginTransactionAsync();

            await ctx.Products.Where(p => p.Id == id).ForUpdate().FirstOrDefaultAsync();
            var lockedIdx = cap.Commands.Count - 1;

            await ctx.Products.Where(p => p.Id == id).FirstOrDefaultAsync();
            var plainIdx = cap.Commands.Count - 1;

            cap.Commands[lockedIdx].Should().Contain("__efcore_locking");
            cap.Commands[plainIdx].Should().NotContain("__efcore_locking");

            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task PlainQuery_WhenSurroundsLockedQuery_ShouldEmitLockOnlyForLockedQuery()
    {
        var (ctx, cap) = CreateContextWithCapture();
        await using (ctx)
        {
            var (_, id) = await SeedAsync(ctx);

            await using var tx = await ctx.Database.BeginTransactionAsync();

            await ctx.Products.Where(p => p.Id == id).FirstOrDefaultAsync();
            var firstIdx = cap.Commands.Count - 1;

            await ctx.Products.Where(p => p.Id == id).ForUpdate().FirstOrDefaultAsync();
            var secondIdx = cap.Commands.Count - 1;

            await ctx.Products.Where(p => p.Id == id).FirstOrDefaultAsync();
            var thirdIdx = cap.Commands.Count - 1;

            cap.Commands[firstIdx].Should().NotContain("__efcore_locking");
            cap.Commands[secondIdx].Should().Contain("__efcore_locking");
            cap.Commands[thirdIdx].Should().NotContain("__efcore_locking");

            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task ForUpdate_WhenThrowsDueToMissingTransaction_ThenPlainQuery_ShouldSucceedWithoutLock()
    {
        var (ctx, cap) = CreateContextWithCapture();
        await using (ctx)
        {
            await SeedAsync(ctx);

            // No transaction — ForUpdate must throw
            Func<Task> badAct = async () => await ctx.Products.ForUpdate().FirstOrDefaultAsync();
            await badAct.Should().ThrowAsync<LockingConfigurationException>();

            // Subsequent plain query in its own transaction must succeed cleanly
            await using var tx = await ctx.Database.BeginTransactionAsync();
            var products = await ctx.Products.ToListAsync();

            products.Should().NotBeEmpty();
            cap.LastCommand.Should().NotContain("__efcore_locking");

            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task ForUpdate_WhenExecutedTwiceSequentially_ShouldUseOwnOptionsEachTime()
    {
        var (ctx, cap) = CreateContextWithCapture();
        await using (ctx)
        {
            var (_, id) = await SeedAsync(ctx);

            await using var tx = await ctx.Database.BeginTransactionAsync();

            // First: Wait behavior
            await ctx.Products.Where(p => p.Id == id).ForUpdate().FirstOrDefaultAsync();
            var firstIdx = cap.Commands.Count - 1;

            // Second: NoWait behavior
            await ctx.Products.Where(p => p.Id == id).ForUpdate(LockBehavior.NoWait).FirstOrDefaultAsync();
            var secondIdx = cap.Commands.Count - 1;

            // Tag format: __efcore_locking:{LockMode}:{LockBehavior}:{timeout_ms}
            cap.Commands[firstIdx].Should().Contain("__efcore_locking:ForUpdate:Wait:");
            cap.Commands[secondIdx].Should().Contain("__efcore_locking:ForUpdate:NoWait:");

            await tx.RollbackAsync();
        }
    }
}
