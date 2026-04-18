using AwesomeAssertions;
using EntityFrameworkCore.Locking.MySql.Tests.Fixtures;
using EntityFrameworkCore.Locking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Locking.MySql.Tests;

[Collection("MySql")]
public class ContainerSmokeTest(MySqlFixture fixture)
{
    [Fact]
    public async Task Container_StartsAndAcceptsConnections()
    {
        var serverVersion = ServerVersion.AutoDetect(fixture.ConnectionString);
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseMySql(fixture.ConnectionString, serverVersion)
            .Options;

        await using var ctx = new TestDbContext(options);
        await ctx.Database.EnsureCreatedAsync();

        var canConnect = await ctx.Database.CanConnectAsync();
        canConnect.Should().BeTrue();
    }
}

[CollectionDefinition("MySql")]
public class MySqlCollection : ICollectionFixture<MySqlFixture> { }
