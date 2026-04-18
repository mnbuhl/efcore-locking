using AwesomeAssertions;
using EntityFrameworkCore.Locking.MySql.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.MySql.Tests;

[Collection("MySql")]
public class IntegrationTests(MySqlFixture fixture) : AbstractIntegrationTests
{
    protected override TestDbContext CreateContext()
    {
        var serverVersion = ServerVersion.AutoDetect(fixture.ConnectionString);
        return new(new DbContextOptionsBuilder<TestDbContext>()
            .UseMySql(fixture.ConnectionString, serverVersion)
            .UseLocking()
            .Options);
    }

    // --- MySQL-specific SQL assertions ---

    [Fact]
    public async Task ForUpdate_GeneratesExactSql()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
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
        await ctx.Database.EnsureCreatedAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        ctx.Products.Where(p => p.Id == 1).ForUpdate(LockBehavior.NoWait).ToQueryString()
            .Should().Contain("FOR UPDATE NOWAIT");
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForUpdate_SkipLocked_GeneratesExactSql()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        ctx.Products.Where(p => p.Id == 1).ForUpdate(LockBehavior.SkipLocked).ToQueryString()
            .Should().Contain("FOR UPDATE SKIP LOCKED");
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ForShare_GeneratesExactSql()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        var sql = ctx.Products.Where(p => p.Id == 1).ForShare().ToQueryString();
        sql.Should().Contain("FOR SHARE");
        sql.Should().NotContain("FOR UPDATE");
        await tx.RollbackAsync();
    }
}
