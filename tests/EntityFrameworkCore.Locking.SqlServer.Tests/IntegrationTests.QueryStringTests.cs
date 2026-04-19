using AwesomeAssertions;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.SqlServer.Tests;

public partial class IntegrationTests
{
    [Fact]
    public async Task ForUpdate_GeneratesExactSql()
    {
        await using var ctx = CreateContext();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        var sql = ctx.Products.Where(p => p.Id == 1).ForUpdate().ToQueryString();
        sql.Should().Contain("WITH (UPDLOCK, HOLDLOCK, ROWLOCK)");
        sql.Should().NotContain("FOR UPDATE");
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_SkipLocked_GeneratesReadPastHint()
    {
        await using var ctx = CreateContext();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        var sql = ctx.Products.Where(p => p.Id == 1).ForUpdate(LockBehavior.SkipLocked).ToQueryString();
        sql.Should().Contain("WITH (UPDLOCK, ROWLOCK, READPAST)");
        sql.Should().NotContain("HOLDLOCK");
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_CapturedSql_ContainsUpdlock()
    {
        var (ctx, cap) = CreateContextWithCapture();
        await using var _ = ctx;
        await ctx.Database.EnsureCreatedAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        await ctx.Products.Where(p => p.Id == 1).ForUpdate().FirstOrDefaultAsync();

        cap.Commands.Should().NotBeEmpty();
        cap.LastCommand.Should().Contain("UPDLOCK");
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_NoWait_CapturedSql_ContainsSetLockTimeout0()
    {
        var (ctx, cap) = CreateContextWithCapture();
        await using var _ = ctx;
        await ctx.Database.EnsureCreatedAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        await ctx.Products.Where(p => p.Id == 1).ForUpdate(LockBehavior.NoWait).FirstOrDefaultAsync();

        cap.Commands.Should().Contain(c => c.Contains("SET LOCK_TIMEOUT 0"));
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_WithTimeout_CapturedSql_ContainsSetLockTimeout()
    {
        var (ctx, cap) = CreateContextWithCapture();
        await using var _ = ctx;
        await ctx.Database.EnsureCreatedAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        await ctx
            .Products.Where(p => p.Id == 1)
            .ForUpdate(LockBehavior.Wait, TimeSpan.FromMilliseconds(500))
            .FirstOrDefaultAsync();

        cap.Commands.Should().Contain(c => c.Contains("SET LOCK_TIMEOUT 500"));
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task SaveChanges_CapturedSql_DoesNotContainUpdlock()
    {
        var (ctx, cap) = CreateContextWithCapture();
        await using var _ = ctx;
        await ctx.Database.EnsureCreatedAsync();

        await using var tx = await ctx.Database.BeginTransactionAsync();
        await ctx.Products.Where(p => p.Id == 999).ForUpdate().FirstOrDefaultAsync();

        cap.Clear();
        var cat = new Category { Name = "Cap" };
        ctx.Categories.Add(cat);
        await ctx.SaveChangesAsync();

        cap.Commands.Should().NotContain(c => c.Contains("UPDLOCK"));
        await tx.CommitAsync();
    }
}
