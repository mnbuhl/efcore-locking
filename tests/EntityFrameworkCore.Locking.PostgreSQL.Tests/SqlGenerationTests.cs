using EntityFrameworkCore.Locking.PostgreSQL;
using Xunit;

namespace EntityFrameworkCore.Locking.PostgreSQL.Tests;

public class SqlGenerationTests
{
    private readonly PostgresLockSqlGenerator _generator = new();

    [Theory]
    [InlineData(LockMode.ForUpdate, LockBehavior.Wait, "FOR UPDATE")]
    [InlineData(LockMode.ForUpdate, LockBehavior.SkipLocked, "FOR UPDATE SKIP LOCKED")]
    [InlineData(LockMode.ForUpdate, LockBehavior.NoWait, "FOR UPDATE NOWAIT")]
    [InlineData(LockMode.ForShare, LockBehavior.Wait, "FOR SHARE")]
    [InlineData(LockMode.ForShare, LockBehavior.SkipLocked, "FOR SHARE SKIP LOCKED")]
    [InlineData(LockMode.ForShare, LockBehavior.NoWait, "FOR SHARE NOWAIT")]
    public void GenerateLockClause_ReturnsExpectedSql(LockMode mode, LockBehavior behavior, string expected)
    {
        var options = new LockOptions { Mode = mode, Behavior = behavior };
        Assert.Equal(expected, _generator.GenerateLockClause(options));
    }

    [Fact]
    public void GeneratePreStatementSql_WithTimeout_ReturnsSetLocalLockTimeout()
    {
        var options = new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.Wait, Timeout = TimeSpan.FromMilliseconds(500) };
        var sql = _generator.GeneratePreStatementSql(options);
        Assert.Equal("SET LOCAL lock_timeout = '500ms'", sql);
    }

    [Fact]
    public void GeneratePreStatementSql_NoTimeout_ReturnsNull()
    {
        var options = new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.Wait };
        Assert.Null(_generator.GeneratePreStatementSql(options));
    }

    [Fact]
    public void GeneratePreStatementSql_NoWait_ReturnsNull()
    {
        var options = new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.NoWait };
        Assert.Null(_generator.GeneratePreStatementSql(options));
    }

    [Fact]
    public void SupportsLockOptions_AlwaysTrue()
    {
        var options = new LockOptions { Mode = LockMode.ForUpdate };
        Assert.True(_generator.SupportsLockOptions(options));
    }
}
