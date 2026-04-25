using AwesomeAssertions;
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
    [InlineData(LockMode.ForNoKeyUpdate, LockBehavior.Wait, "FOR NO KEY UPDATE")]
    [InlineData(LockMode.ForNoKeyUpdate, LockBehavior.SkipLocked, "FOR NO KEY UPDATE SKIP LOCKED")]
    [InlineData(LockMode.ForNoKeyUpdate, LockBehavior.NoWait, "FOR NO KEY UPDATE NOWAIT")]
    [InlineData(LockMode.ForKeyShare, LockBehavior.Wait, "FOR KEY SHARE")]
    [InlineData(LockMode.ForKeyShare, LockBehavior.SkipLocked, "FOR KEY SHARE SKIP LOCKED")]
    [InlineData(LockMode.ForKeyShare, LockBehavior.NoWait, "FOR KEY SHARE NOWAIT")]
    public void GenerateLockClause_ReturnsExpectedSql(LockMode mode, LockBehavior behavior, string expected)
    {
        var options = new LockOptions { Mode = mode, Behavior = behavior };
        _generator.GenerateLockClause(options).Should().Be(expected);
    }

    [Fact]
    public void GeneratePreStatementSql_WithTimeout_ReturnsSetLocalLockTimeout()
    {
        var options = new LockOptions
        {
            Mode = LockMode.ForUpdate,
            Behavior = LockBehavior.Wait,
            Timeout = TimeSpan.FromMilliseconds(500),
        };
        _generator.GeneratePreStatementSql(options).Should().Be("SET LOCAL lock_timeout = '500ms'");
    }

    [Fact]
    public void GeneratePreStatementSql_NoTimeout_ReturnsNull()
    {
        var options = new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.Wait };
        _generator.GeneratePreStatementSql(options).Should().BeNull();
    }

    [Fact]
    public void GeneratePreStatementSql_NoWait_ReturnsNull()
    {
        var options = new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.NoWait };
        _generator.GeneratePreStatementSql(options).Should().BeNull();
    }

    [Fact]
    public void SupportsLockOptions_AlwaysTrue()
    {
        _generator.SupportsLockOptions(new LockOptions { Mode = LockMode.ForUpdate }).Should().BeTrue();
        _generator.SupportsLockOptions(new LockOptions { Mode = LockMode.ForShare }).Should().BeTrue();
        _generator
            .SupportsLockOptions(new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.SkipLocked })
            .Should()
            .BeTrue();
        _generator.SupportsLockOptions(new LockOptions { Mode = LockMode.ForNoKeyUpdate }).Should().BeTrue();
        _generator.SupportsLockOptions(new LockOptions { Mode = LockMode.ForKeyShare }).Should().BeTrue();
    }

    [Theory]
    [InlineData(1000, "1000ms")]
    [InlineData(250, "250ms")]
    [InlineData(5000, "5000ms")]
    public void GeneratePreStatementSql_VariousTimeouts_CorrectMs(int ms, string expectedSuffix)
    {
        var options = new LockOptions
        {
            Mode = LockMode.ForUpdate,
            Behavior = LockBehavior.Wait,
            Timeout = TimeSpan.FromMilliseconds(ms),
        };
        _generator.GeneratePreStatementSql(options).Should().Be($"SET LOCAL lock_timeout = '{expectedSuffix}'");
    }
}
