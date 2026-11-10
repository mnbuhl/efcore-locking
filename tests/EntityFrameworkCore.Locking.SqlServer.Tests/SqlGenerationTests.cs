using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
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
        var opts = new LockOptions
        {
            Mode = LockMode.ForUpdate,
            Behavior = LockBehavior.Wait,
            Timeout = TimeSpan.FromMilliseconds(500),
        };
        _generator.GeneratePreStatementSql(opts).Should().Be("SET LOCK_TIMEOUT 500");
    }

    [Theory]
    [InlineData(100, "SET LOCK_TIMEOUT 100")]
    [InlineData(1000, "SET LOCK_TIMEOUT 1000")]
    [InlineData(5000, "SET LOCK_TIMEOUT 5000")]
    public void GeneratePreStatementSql_VariousTimeouts_CorrectMs(int ms, string expected)
    {
        var opts = new LockOptions
        {
            Mode = LockMode.ForUpdate,
            Behavior = LockBehavior.Wait,
            Timeout = TimeSpan.FromMilliseconds(ms),
        };
        _generator.GeneratePreStatementSql(opts).Should().Be(expected);
    }

    [Theory]
    [InlineData(LockBehavior.Wait, true)]
    [InlineData(LockBehavior.NoWait, true)]
    [InlineData(LockBehavior.SkipLocked, false)]
    public void SupportsLockOptions_ForShare_ValidatesBehavior(LockBehavior behavior, bool supported)
    {
        _generator
            .SupportsLockOptions(new LockOptions { Mode = LockMode.ForShare, Behavior = behavior })
            .Should()
            .Be(supported);
    }

    [Theory]
    [InlineData(LockBehavior.Wait, null)]
    [InlineData(LockBehavior.NoWait, "SET LOCK_TIMEOUT 0")]
    public void GenerateLockClause_ForShare_ReturnsSharedHint(LockBehavior behavior, string? preStatement)
    {
        var opts = new LockOptions { Mode = LockMode.ForShare, Behavior = behavior };
        _generator.GenerateLockClause(opts).Should().Be("WITH (HOLDLOCK, ROWLOCK)");
        _generator.GeneratePreStatementSql(opts).Should().Be(preStatement);
    }

    [Fact]
    public void GeneratePreStatementSql_ForShare_WithTimeout_ReturnsSetLockTimeoutMs()
    {
        var opts = new LockOptions { Mode = LockMode.ForShare, Timeout = TimeSpan.FromMilliseconds(500) };
        _generator.GeneratePreStatementSql(opts).Should().Be("SET LOCK_TIMEOUT 500");
    }

    [Fact]
    public void SupportsLockOptions_SkipLocked_ReturnsTrue()
    {
        _generator
            .SupportsLockOptions(new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.SkipLocked })
            .Should()
            .BeTrue();
    }

    [Fact]
    public void GenerateLockClause_SkipLocked_ReturnsReadPastHint()
    {
        var opts = new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.SkipLocked };
        _generator.GenerateLockClause(opts).Should().Be("WITH (UPDLOCK, ROWLOCK, READPAST)");
    }

    [Fact]
    public void SupportsLockOptions_ForUpdate_Wait_ReturnsTrue()
    {
        _generator
            .SupportsLockOptions(new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.Wait })
            .Should()
            .BeTrue();
    }

    [Fact]
    public void SupportsLockOptions_ForUpdate_NoWait_ReturnsTrue()
    {
        _generator
            .SupportsLockOptions(new LockOptions { Mode = LockMode.ForUpdate, Behavior = LockBehavior.NoWait })
            .Should()
            .BeTrue();
    }

    [Theory]
    [InlineData(LockBehavior.Wait)]
    [InlineData(LockBehavior.NoWait)]
    public void ForShare_ToQueryString_EmitsSharedHints(LockBehavior behavior)
    {
        using var ctx = new TestDbContext(
            new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlServer(
                    "Server=localhost;Database=SqlGeneration;Integrated Security=true;TrustServerCertificate=true"
                )
                .UseLocking()
                .Options
        );
        var sql = ctx.Products.Where(p => p.Id == 1).ForShare(behavior).ToQueryString();
        sql.Should().Contain("WITH (HOLDLOCK, ROWLOCK)");
        sql.Should().NotContain("UPDLOCK");
    }

    [Fact]
    public void ForShare_SkipLocked_ToQueryString_ThrowsConfigurationException()
    {
        using var ctx = new TestDbContext(
            new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlServer(
                    "Server=localhost;Database=SqlGeneration;Integrated Security=true;TrustServerCertificate=true"
                )
                .UseLocking()
                .Options
        );
        Action generate = () => ctx.Products.ForShare(LockBehavior.SkipLocked).ToQueryString();
        generate.Should().Throw<LockingConfigurationException>();
    }
}
