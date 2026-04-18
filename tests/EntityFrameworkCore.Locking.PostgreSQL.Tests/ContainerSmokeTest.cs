using EntityFrameworkCore.Locking.PostgreSQL.Tests.Fixtures;
using Xunit;
using Microsoft.EntityFrameworkCore;

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
        Assert.True(canConnect);
    }
}

[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture> { }
