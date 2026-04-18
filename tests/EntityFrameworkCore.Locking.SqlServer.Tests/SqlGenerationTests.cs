using AwesomeAssertions;
using EntityFrameworkCore.Locking.SqlServer;
using Xunit;

namespace EntityFrameworkCore.Locking.SqlServer.Tests;

public class SqlGenerationTests
{
    private readonly SqlServerLockSqlGenerator _generator = new();

    [Fact]
    public void GenerateLockClause_ForUpdate_ReturnsTableHint()
    {
        var opts = new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.Wait };
        _generator.GenerateLockClause(opts).Should().Be("WITH (UPDLOCK, HOLDLOCK, ROWLOCK)");
    }

    [Fact]
    public void GeneratePreStatementSql_NoTimeout_ReturnsNull()
    {
        var opts = new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.Wait };
        _generator.GeneratePreStatementSql(opts).Should().BeNull();
    }

    [Fact]
    public void GeneratePreStatementSql_NoWait_ReturnsSetLockTimeout0()
    {
        var opts = new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.NoWait };
        _generator.GeneratePreStatementSql(opts).Should().Be("SET LOCK_TIMEOUT 0");
    }

    [Fact]
    public void GeneratePreStatementSql_WithTimeout_ReturnsSetLockTimeoutMs()
    {
        var opts = new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.Wait, Timeout = TimeSpan.FromMilliseconds(500) };
        _generator.GeneratePreStatementSql(opts).Should().Be("SET LOCK_TIMEOUT 500");
    }

    [Theory]
    [InlineData(100, "SET LOCK_TIMEOUT 100")]
    [InlineData(1000, "SET LOCK_TIMEOUT 1000")]
    [InlineData(5000, "SET LOCK_TIMEOUT 5000")]
    public void GeneratePreStatementSql_VariousTimeouts_CorrectMs(int ms, string expected)
    {
        var opts = new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.Wait, Timeout = TimeSpan.FromMilliseconds(ms) };
        _generator.GeneratePreStatementSql(opts).Should().Be(expected);
    }

    [Fact]
    public void SupportsLockOptions_ForShare_ReturnsFalse()
    {
        _generator.SupportsLockOptions(new LockOptions { Mode = LockMode.ForShare }).Should().BeFalse();
    }

    [Fact]
    public void SupportsLockOptions_SkipLocked_ReturnsFalse()
    {
        _generator.SupportsLockOptions(new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.SkipLocked }).Should().BeFalse();
    }

    [Fact]
    public void SupportsLockOptions_ForUpdate_Wait_ReturnsTrue()
    {
        _generator.SupportsLockOptions(new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.Wait }).Should().BeTrue();
    }

    [Fact]
    public void SupportsLockOptions_ForUpdate_NoWait_ReturnsTrue()
    {
        _generator.SupportsLockOptions(new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.NoWait }).Should().BeTrue();
    }
}
