using AwesomeAssertions;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.Internal;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.MySql.Tests;

public class WrappedQueryTests
{
    [Theory]
    [InlineData(LockMode.ForUpdate, "FOR UPDATE")]
    [InlineData(LockMode.ForShare, "FOR SHARE")]
    public void Lock_ThroughWrappingProvider_GeneratesSameSql(LockMode mode, string expectedClause)
    {
        using var ctx = new TestDbContext(
            new DbContextOptionsBuilder<TestDbContext>()
                .UseMySql("Server=localhost;Database=SqlGeneration", new MySqlServerVersion(new Version(8, 0, 36)))
                .UseLocking()
                .Options
        );
        var timeout = TimeSpan.FromSeconds(2);
        IQueryable<Product> ApplyLock(IQueryable<Product> source) =>
            mode switch
            {
                LockMode.ForUpdate => source.ForUpdate(LockBehavior.NoWait, timeout),
                LockMode.ForShare => source.ForShare(LockBehavior.NoWait, timeout),
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };

        var direct = ApplyLock(ctx.Products.Where(p => p.Id == 1));
        LockContext.Current = null;
        var expectedSql = direct.ToQueryString();
        IQueryable<Product> wrapped = new WrappingQueryable<Product>(ctx.Products);
        var query = ApplyLock(wrapped.Where(p => p.Id == 1)).OrderBy(p => p.Id).Take(1);
        LockContext.Current = null; // SQL generation must recover the lock from the expression tag.
        var sql = query.ToQueryString();

        sql.Should().Contain(expectedClause);
        sql.Should().Contain("__efcore_locking:" + mode + ":NoWait:2000");
        // Also compare identical query shapes, including any provider-specific SQL details.
        LockContext.Current = null;
        ApplyLock(wrapped.Where(p => p.Id == 1)).ToQueryString().Should().Be(expectedSql);
    }

    [Theory]
    [InlineData(LockMode.ForUpdate)]
    [InlineData(LockMode.ForShare)]
    public void Lock_OnLinqToObjects_ThrowsConfigurationException(LockMode mode)
    {
        LockContext.Current = null;
        var source = Array.Empty<Product>().AsQueryable();
        var timeout = TimeSpan.FromSeconds(2);
        Action apply = () =>
        {
            _ = mode switch
            {
                LockMode.ForUpdate => source.ForUpdate(LockBehavior.NoWait, timeout),
                LockMode.ForShare => source.ForShare(LockBehavior.NoWait, timeout),
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };
        };
        apply.Should().Throw<LockingConfigurationException>().WithMessage("*EF Core query*");
        LockContext.Current.Should().BeNull();
    }
}
