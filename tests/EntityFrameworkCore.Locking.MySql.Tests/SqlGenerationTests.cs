using AwesomeAssertions;
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
        _generator.GenerateLockClause(opts).Should().Be(expected);
    }

    [Fact]
    public void GeneratePreStatementSql_NoTimeout_ReturnsNull()
    {
        var opts = new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.Wait };
        _generator.GeneratePreStatementSql(opts).Should().BeNull();
    }

    [Fact]
    public void GeneratePreStatementSql_WithTimeout_ReturnsSetSessionTimeout()
    {
        var opts = new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.Wait, Timeout = TimeSpan.FromSeconds(5) };
        _generator.GeneratePreStatementSql(opts).Should().Be("SET SESSION innodb_lock_wait_timeout = 5");
    }

    [Fact]
    public void GeneratePreStatementSql_SubSecondTimeout_CeilsToOne()
    {
        var opts = new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.Wait, Timeout = TimeSpan.FromMilliseconds(200) };
        _generator.GeneratePreStatementSql(opts).Should().Be("SET SESSION innodb_lock_wait_timeout = 1");
    }

    [Theory]
    [InlineData(1500, 2)]
    [InlineData(1000, 1)]
    [InlineData(999, 1)]
    [InlineData(10000, 10)]
    public void GeneratePreStatementSql_Timeout_CeilsToWholeSeconds(int ms, int expectedSeconds)
    {
        var opts = new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.Wait, Timeout = TimeSpan.FromMilliseconds(ms) };
        _generator.GeneratePreStatementSql(opts).Should().Be($"SET SESSION innodb_lock_wait_timeout = {expectedSeconds}");
    }

    [Fact]
    public void SupportsLockOptions_AlwaysTrue()
    {
        _generator.SupportsLockOptions(new LockOptions { Mode = LockMode.ForUpdate }).Should().BeTrue();
        _generator.SupportsLockOptions(new LockOptions { Mode = LockMode.ForShare }).Should().BeTrue();
        _generator.SupportsLockOptions(new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.SkipLocked }).Should().BeTrue();
    }
}
