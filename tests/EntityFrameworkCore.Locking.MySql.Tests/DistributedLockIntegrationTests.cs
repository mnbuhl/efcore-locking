using AwesomeAssertions;
using EntityFrameworkCore.Locking.MySql.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.MySql.Tests;

[Collection("MySql")]
public class DistributedLockIntegrationTests(MySqlFixture fixture) : DistributedLockIntegrationTestsBase
{
    // MySQL GET_LOCK timeout is integer seconds; 500ms rounds down to 0 (non-blocking try).
    protected override TimeSpan DistributedLockAcquireTimeout => TimeSpan.FromSeconds(1);

    protected override TestDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<TestDbContext>()
                .UseMySql(fixture.ConnectionString, ServerVersion.AutoDetect(fixture.ConnectionString))
                .UseLocking()
                .Options
        );

    // --- MySQL-specific ---

    [Fact]
    public async Task LongKey_ExceededMysqlLimit_HashesCorrectly()
    {
        // Key > 64 chars is hashed to lock:<hex58> (64 chars total)
        var longKey = new string('x', 100);
        await using var ctx = CreateContext();
        await using var handle = await ctx.Database.AcquireDistributedLockAsync(longKey);
        handle.Should().NotBeNull();
        handle.Key.Should().Be(longKey); // public Key is the original, not encoded
    }
}
