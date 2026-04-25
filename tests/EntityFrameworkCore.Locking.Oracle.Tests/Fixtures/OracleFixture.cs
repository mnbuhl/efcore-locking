using Testcontainers.Oracle;
using Xunit;

namespace EntityFrameworkCore.Locking.Oracle.Tests.Fixtures;

/// <summary>
/// xUnit fixture booting an Oracle Database Free container via Testcontainers.
/// Uses gvenzl/oracle-free:23-slim-faststart — smaller/faster than the official image and suitable for CI.
/// Grants EXECUTE ON DBMS_LOCK to the app user so DBMS_LOCK-based advisory locks work;
/// without this grant, calls surface as ORA-06550.
///
/// We build the connection string ourselves targeting the FREEPDB1 pluggable database because
/// Testcontainers.Oracle's default connection string resolves to a service the listener does
/// not register under these images (ORA-12514). FREEPDB1 is where the gvenzl entrypoint
/// creates the APP_USER when WithUsername is set.
/// </summary>
public sealed class OracleFixture : IAsyncLifetime
{
    private const string AppUsername = "testuser";
    private const string AppPassword = "testuser";
    private const string AdminPassword = "oracle";
    private const int OraclePort = 1521;
    private const string PluggableDatabase = "FREEPDB1";

    private readonly OracleContainer _container = new OracleBuilder()
        .WithImage("gvenzl/oracle-free:23-slim-faststart")
        .WithUsername(AppUsername)
        .WithPassword(AppPassword)
        .WithEnvironment("ORACLE_PASSWORD", AdminPassword)
        .Build();

    public string ConnectionString =>
        $"User Id={AppUsername};Password={AppPassword};"
        + $"Data Source=//{_container.Hostname}:{_container.GetMappedPublicPort(OraclePort)}/{PluggableDatabase}";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await GrantDbmsLockExecuteAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>
    /// Grants EXECUTE ON SYS.DBMS_LOCK to the app user by running sqlplus inside the container
    /// as SYS AS SYSDBA. Running inside the container avoids listener/service-name resolution
    /// issues and doesn't require WITH GRANT OPTION (SYSTEM would fail with ORA-01031).
    /// </summary>
    private async Task GrantDbmsLockExecuteAsync()
    {
        var grantUser = AppUsername.ToUpperInvariant();
        var script = $"GRANT EXECUTE ON SYS.DBMS_LOCK TO {grantUser};\nEXIT;\n";
        var result = await _container.ExecAsync([
            "sh",
            "-c",
            $"echo \"{script.Replace("\"", "\\\"")}\" | sqlplus -s 'SYS/{AdminPassword}@//localhost:{OraclePort}/{PluggableDatabase} AS SYSDBA'",
        ]);

        if (result.ExitCode != 0 || result.Stdout.Contains("ORA-") || result.Stderr.Contains("ORA-"))
            throw new InvalidOperationException(
                $"Failed to grant EXECUTE ON DBMS_LOCK to {grantUser}.\n"
                    + $"ExitCode: {result.ExitCode}\nStdout: {result.Stdout}\nStderr: {result.Stderr}"
            );
    }
}
