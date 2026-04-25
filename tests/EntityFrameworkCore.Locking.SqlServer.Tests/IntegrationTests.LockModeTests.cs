using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.SqlServer.Tests;

public partial class IntegrationTests
{
    [Fact]
    public async Task ForShare_ThrowsLockingConfigurationException()
    {
        await using var ctx = CreateContext();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        Func<Task> act = async () => await ctx.Products.Where(p => p.Id == 1).ForShare().FirstOrDefaultAsync();
        await act.Should().ThrowAsync<LockingConfigurationException>();
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
    public async Task ForUpdate_SkipLocked_ThrowsLockingConfigurationException_WhenNotSupported()
    {
        // SQL Server supports SkipLocked via READPAST — this test verifies it does NOT throw
        await using var ctx = CreateContext();
        var (_, id) = await SeedAsync(ctx);

        await using var tx = await ctx.Database.BeginTransactionAsync();
        var row = await ctx.Products.Where(p => p.Id == id).ForUpdate(LockBehavior.SkipLocked).FirstOrDefaultAsync();

        row.Should().NotBeNull();
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_WhenOrderByTakeLockBehaviors_WhenInsideTransaction_ShouldEmitCorrectSql()
    {
        var (ctx, cap) = CreateContextWithCapture();
        await using (ctx)
        {
            var (catId, _) = await SeedAsync(ctx, categoryName: "SqlCheck");

            await using var tx = await ctx.Database.BeginTransactionAsync();

            var result = await ctx
                .Products.Where(p => p.CategoryId == catId)
                .OrderBy(p => p.Id)
                .Take(1)
                .ForUpdate(LockBehavior.NoWait)
                .ToListAsync();

            result.Should().HaveCount(1);
            // SQL Server NoWait: SET LOCK_TIMEOUT 0 as pre-statement, UPDLOCK in FROM clause
            cap.Commands.Should().Contain(c => c.Contains("SET LOCK_TIMEOUT 0"));
            cap.LastCommand.Should().Contain("UPDLOCK");

            await tx.RollbackAsync();
        }
    }
}
