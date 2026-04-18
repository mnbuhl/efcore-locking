using AwesomeAssertions;
using EntityFrameworkCore.Locking.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.PostgreSQL.Tests;

public partial class IntegrationTests
{
    // ToQueryString() tests verify EF Core generates the expected SQL fragments.
    // SqlCapture tests verify the actual executed SQL (post-interceptor modifications).

    [Fact]
    public void ForUpdate_GeneratesExactSql()
    {
        using var ctx = CreateContext();
        var sql = ctx.Products.Where(p => p.Id == 1).ForUpdate().ToQueryString();

        sql.Should().Contain("FOR UPDATE");
        sql.Should().NotContain("FOR SHARE");
        sql.Should().NotContain("NOWAIT");
        sql.Should().NotContain("SKIP LOCKED");
    }

    [Fact]
    public void ForUpdate_NoWait_GeneratesExactSql()
    {
        using var ctx = CreateContext();
        ctx.Products.Where(p => p.Id == 1).ForUpdate(LockBehavior.NoWait).ToQueryString()
            .Should().Contain("FOR UPDATE NOWAIT");
    }

    [Fact]
    public void ForUpdate_SkipLocked_GeneratesExactSql()
    {
        using var ctx = CreateContext();
        ctx.Products.Where(p => p.Id == 1).ForUpdate(LockBehavior.SkipLocked).ToQueryString()
            .Should().Contain("FOR UPDATE SKIP LOCKED");
    }

    [Fact]
    public void ForShare_GeneratesExactSql()
    {
        using var ctx = CreateContext();
        var sql = ctx.Products.Where(p => p.Id == 1).ForShare().ToQueryString();
        sql.Should().Contain("FOR SHARE");
        sql.Should().NotContain("FOR UPDATE");
    }

    [Fact]
    public void ForShare_NoWait_GeneratesExactSql()
    {
        using var ctx = CreateContext();
        ctx.Products.Where(p => p.Id == 1).ForShare(LockBehavior.NoWait).ToQueryString()
            .Should().Contain("FOR SHARE NOWAIT");
    }

    [Fact]
    public void ForShare_SkipLocked_GeneratesExactSql()
    {
        using var ctx = CreateContext();
        ctx.Products.Where(p => p.Id == 1).ForShare(LockBehavior.SkipLocked).ToQueryString()
            .Should().Contain("FOR SHARE SKIP LOCKED");
    }

    [Fact]
    public void ForNoKeyUpdate_GeneratesExactSql()
    {
        using var ctx = CreateContext();
        ctx.Products.Where(p => p.Id == 1).ForNoKeyUpdate().ToQueryString()
            .Should().Contain("FOR NO KEY UPDATE");
    }

    [Fact]
    public void ForNoKeyUpdate_NoWait_GeneratesExactSql()
    {
        using var ctx = CreateContext();
        ctx.Products.Where(p => p.Id == 1).ForNoKeyUpdate(LockBehavior.NoWait).ToQueryString()
            .Should().Contain("FOR NO KEY UPDATE NOWAIT");
    }

    [Fact]
    public void ForNoKeyUpdate_SkipLocked_GeneratesExactSql()
    {
        using var ctx = CreateContext();
        ctx.Products.Where(p => p.Id == 1).ForNoKeyUpdate(LockBehavior.SkipLocked).ToQueryString()
            .Should().Contain("FOR NO KEY UPDATE SKIP LOCKED");
    }

    [Fact]
    public void ForKeyShare_GeneratesExactSql()
    {
        using var ctx = CreateContext();
        ctx.Products.Where(p => p.Id == 1).ForKeyShare().ToQueryString()
            .Should().Contain("FOR KEY SHARE");
    }

    [Fact]
    public void ForKeyShare_NoWait_GeneratesExactSql()
    {
        using var ctx = CreateContext();
        ctx.Products.Where(p => p.Id == 1).ForKeyShare(LockBehavior.NoWait).ToQueryString()
            .Should().Contain("FOR KEY SHARE NOWAIT");
    }

    [Fact]
    public void ForUpdate_WithLeftJoin_GeneratesForUpdateOf()
    {
        using var ctx = CreateContext();
        var sql = ctx.Products.Include(p => p.OrderLines).Where(p => p.Id == 1).ForUpdate().ToQueryString();
        sql.Should().Contain("FOR UPDATE OF");
    }

    [Fact]
    public void ForUpdate_WithoutJoin_DoesNotGenerateOf()
    {
        using var ctx = CreateContext();
        var sql = ctx.Products.Where(p => p.Id == 1).ForUpdate().ToQueryString();
        sql.Should().Contain("FOR UPDATE");
        sql.Should().NotContain("FOR UPDATE OF");
    }

    [Fact]
    public async Task ForUpdate_CapturedSql_ContainsForUpdate()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        var (capture, captureCtx) = (new EntityFrameworkCore.Locking.Tests.Infrastructure.SqlCapture(),
            CreateContextWithCapture());
        _ = captureCtx.capture;

        var (ctx2, cap) = CreateContextWithCapture();
        await using var tx = await ctx2.Database.BeginTransactionAsync();
        await ctx2.Products.Where(p => p.Id == 1).ForUpdate().FirstOrDefaultAsync();

        cap.Commands.Should().NotBeEmpty();
        cap.LastCommand.Should().Contain("FOR UPDATE");
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_WithTimeout_CapturedSql_ContainsSetLocalLockTimeout()
    {
        var (ctx, cap) = CreateContextWithCapture();
        await using var _ = ctx;
        await ctx.Database.EnsureCreatedAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        await ctx.Products.Where(p => p.Id == 1)
            .ForUpdate(LockBehavior.Wait, TimeSpan.FromMilliseconds(200))
            .FirstOrDefaultAsync();

        cap.Commands.Should().Contain(c => c.Contains("lock_timeout"));
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task SaveChanges_CapturedSql_DoesNotContainForUpdate()
    {
        var (ctx, cap) = CreateContextWithCapture();
        await using var _ = ctx;
        await ctx.Database.EnsureCreatedAsync();

        // First do a locking query, then SaveChanges — verify no FOR UPDATE leaks
        await using var tx = await ctx.Database.BeginTransactionAsync();
        await ctx.Products.Where(p => p.Id == 999).ForUpdate().FirstOrDefaultAsync();

        cap.Clear();
        var cat = new EntityFrameworkCore.Locking.Tests.Infrastructure.Category { Name = "Cap" };
        ctx.Categories.Add(cat);
        await ctx.SaveChangesAsync();

        cap.Commands.Should().NotBeEmpty();
        cap.Commands.Should().NotContain(c => c.Contains("FOR UPDATE"));
        await tx.CommitAsync();
    }
}
