using AwesomeAssertions;
using EntityFrameworkCore.Locking.SqlServer.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.SqlServer.Tests;

[Collection("SqlServer")]
public class ContainerSmokeTest(SqlServerFixture fixture)
{
    [Fact]
    public async Task Container_StartsAndAcceptsConnections()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer(fixture.ConnectionString)
            .Options;

        await using var ctx = new TestDbContext(options);
        await ctx.Database.EnsureCreatedAsync();

        (await ctx.Database.CanConnectAsync()).Should().BeTrue();
    }
}

[CollectionDefinition("SqlServer")]
public class SqlServerCollection : ICollectionFixture<SqlServerFixture> { }
