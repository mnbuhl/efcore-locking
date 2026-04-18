using AwesomeAssertions;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.MySql.Tests;

public partial class IntegrationTests
{
    [Fact]
    public async Task ForUpdate_GeneratesExactSql()
    {
        await using var ctx = CreateContext();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        var sql = ctx.Products.Where(p => p.Id == 1).ForUpdate().ToQueryString();
        sql.Should().Contain("FOR UPDATE");
        sql.Should().NotContain("NOWAIT");
        sql.Should().NotContain("SKIP LOCKED");
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_NoWait_GeneratesExactSql()
    {
        await using var ctx = CreateContext();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        ctx.Products.Where(p => p.Id == 1).ForUpdate(LockBehavior.NoWait).ToQueryString()
            .Should().Contain("FOR UPDATE NOWAIT");
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_SkipLocked_GeneratesExactSql()
    {
        await using var ctx = CreateContext();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        ctx.Products.Where(p => p.Id == 1).ForUpdate(LockBehavior.SkipLocked).ToQueryString()
            .Should().Contain("FOR UPDATE SKIP LOCKED");
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForShare_GeneratesExactSql()
    {
        await using var ctx = CreateContext();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        var sql = ctx.Products.Where(p => p.Id == 1).ForShare().ToQueryString();
        sql.Should().Contain("FOR SHARE");
        sql.Should().NotContain("FOR UPDATE");
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_CapturedSql_ContainsForUpdate()
    {
        var (ctx, cap) = CreateContextWithCapture();
        await using var _ = ctx;
        await ctx.Database.EnsureCreatedAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        await ctx.Products.Where(p => p.Id == 1).ForUpdate().FirstOrDefaultAsync();

        cap.Commands.Should().NotBeEmpty();
        cap.LastCommand.Should().Contain("FOR UPDATE");
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_WithTimeout_CapturedSql_ContainsSetSessionTimeout()
    {
        var (ctx, cap) = CreateContextWithCapture();
        await using var _ = ctx;
        await ctx.Database.EnsureCreatedAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        await ctx.Products.Where(p => p.Id == 1)
            .ForUpdate(LockBehavior.Wait, TimeSpan.FromSeconds(2))
            .FirstOrDefaultAsync();

        cap.Commands.Should().Contain(c => c.Contains("innodb_lock_wait_timeout"));
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task SaveChanges_CapturedSql_DoesNotContainForUpdate()
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

        cap.Commands.Should().NotContain(c => c.Contains("FOR UPDATE"));
        await tx.CommitAsync();
    }
}
