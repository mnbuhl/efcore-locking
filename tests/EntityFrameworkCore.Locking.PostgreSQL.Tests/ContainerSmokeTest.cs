using AwesomeAssertions;
using EntityFrameworkCore.Locking.PostgreSQL.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.PostgreSQL.Tests;

[Collection("Postgres")]
public class ContainerSmokeTest(PostgresFixture fixture)
{
    [Fact]
    public async Task Container_StartsAndAcceptsConnections()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;

        await using var ctx = new TestDbContext(options);
        await ctx.Database.EnsureCreatedAsync();

        var canConnect = await ctx.Database.CanConnectAsync();
        canConnect.Should().BeTrue();
    }
}

[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture> { }
