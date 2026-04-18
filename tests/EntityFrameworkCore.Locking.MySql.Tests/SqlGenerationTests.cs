using EntityFrameworkCore.Locking.MySql;
using Xunit;

namespace EntityFrameworkCore.Locking.MySql.Tests;

public class SqlGenerationTests
{
    private readonly MySqlLockSqlGenerator _generator = new();

    [Theory]
    [InlineData(LockMode.ForUpdate, LockBehavior.Wait, "FOR UPDATE")]
    [InlineData(LockMode.ForUpdate, LockBehavior.NoWait, "FOR UPDATE NOWAIT")]
    [InlineData(LockMode.ForUpdate, LockBehavior.SkipLocked, "FOR UPDATE SKIP LOCKED")]
    [InlineData(LockMode.ForShare, LockBehavior.Wait, "FOR SHARE")]
    [InlineData(LockMode.ForShare, LockBehavior.NoWait, "FOR SHARE NOWAIT")]
    [InlineData(LockMode.ForShare, LockBehavior.SkipLocked, "FOR SHARE SKIP LOCKED")]
    public void GenerateLockClause_ReturnsExpectedSql(LockMode mode, LockBehavior behavior, string expected)
    {
        var opts = new LockOptions { Mode = mode, Behavior = behavior };
        Assert.Equal(expected, _generator.GenerateLockClause(opts));
    }

    [Fact]
    public void GeneratePreStatementSql_NoTimeout_ReturnsNull()
    {
        var opts = new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.Wait };
        Assert.Null(_generator.GeneratePreStatementSql(opts));
    }

    [Fact]
    public void GeneratePreStatementSql_WithTimeout_ReturnsSetSessionTimeout()
    {
        var opts = new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.Wait, Timeout = TimeSpan.FromSeconds(5) };
        Assert.Equal("SET SESSION innodb_lock_wait_timeout = 5", _generator.GeneratePreStatementSql(opts));
    }

    [Fact]
    public void GeneratePreStatementSql_SubSecondTimeout_CeilsToOne()
    {
        var opts = new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.Wait, Timeout = TimeSpan.FromMilliseconds(200) };
        Assert.Equal("SET SESSION innodb_lock_wait_timeout = 1", _generator.GeneratePreStatementSql(opts));
    }

    [Fact]
    public void SupportsLockOptions_AlwaysTrue()
    {
        Assert.True(_generator.SupportsLockOptions(new LockOptions { Mode = LockMode.ForUpdate }));
    }
}
