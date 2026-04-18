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
        Assert.Equal("WITH (UPDLOCK, HOLDLOCK, ROWLOCK)", _generator.GenerateLockClause(opts));
    }

    [Fact]
    public void GeneratePreStatementSql_NoTimeout_ReturnsNull()
    {
        var opts = new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.Wait };
        Assert.Null(_generator.GeneratePreStatementSql(opts));
    }

    [Fact]
    public void GeneratePreStatementSql_NoWait_ReturnsSetLockTimeout0()
    {
        var opts = new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.NoWait };
        Assert.Equal("SET LOCK_TIMEOUT 0", _generator.GeneratePreStatementSql(opts));
    }

    [Fact]
    public void GeneratePreStatementSql_WithTimeout_ReturnsSetLockTimeoutMs()
    {
        var opts = new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.Wait, Timeout = TimeSpan.FromMilliseconds(500) };
        Assert.Equal("SET LOCK_TIMEOUT 500", _generator.GeneratePreStatementSql(opts));
    }

    [Fact]
    public void SupportsLockOptions_ForShare_ReturnsFalse()
    {
        Assert.False(_generator.SupportsLockOptions(new LockOptions { Mode = LockMode.ForShare }));
    }

    [Fact]
    public void SupportsLockOptions_SkipLocked_ReturnsFalse()
    {
        Assert.False(_generator.SupportsLockOptions(new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.SkipLocked }));
    }

    [Fact]
    public void SupportsLockOptions_ForUpdate_Wait_ReturnsTrue()
    {
        Assert.True(_generator.SupportsLockOptions(new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.Wait }));
    }
}
