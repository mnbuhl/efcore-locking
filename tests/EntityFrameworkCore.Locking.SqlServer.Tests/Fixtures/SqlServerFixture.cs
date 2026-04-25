using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit;

namespace EntityFrameworkCore.Locking.SqlServer.Tests.Fixtures;

public sealed class SqlServerFixture : IAsyncLifetime
{
    // mcr.microsoft.com/mssql/server:2022-latest is AMD64-only and times out under Rosetta
    // on Apple Silicon. Use azure-sql-edge which has a native ARM64 image and the same wire
    // protocol. The MsSqlBuilder default readiness probe uses sqlcmd which is absent in
    // azure-sql-edge, so we replace it with a TCP-then-login probe.
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/azure-sql-edge:latest")
        .WithWaitStrategy(
            Wait.ForUnixContainer()
                .UntilPortIsAvailable(MsSqlBuilder.MsSqlPort)
                .AddCustomWaitStrategy(new WaitUntilLoginSucceeds())
        )
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private sealed class WaitUntilLoginSucceeds : IWaitUntil
    {
        public async Task<bool> UntilAsync(IContainer container)
        {
            var mssql = (MsSqlContainer)container;
            try
            {
                await using var conn = new SqlConnection(mssql.GetConnectionString());
                await conn.OpenAsync().ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
