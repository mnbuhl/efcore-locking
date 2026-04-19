using Testcontainers.MySql;
using Xunit;

namespace EntityFrameworkCore.Locking.MySql.Tests.Fixtures;

public sealed class MySqlFixture : IAsyncLifetime
{
    private readonly MySqlContainer _container = new MySqlBuilder().WithImage("mysql:8.0").Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
