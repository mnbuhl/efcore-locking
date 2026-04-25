using System.Data.Common;
using Testcontainers.Oracle;
using Xunit;

namespace EntityFrameworkCore.Locking.Oracle.Tests.Fixtures;

/// <summary>
/// xUnit fixture booting an Oracle Database Free container via Testcontainers.
/// Uses gvenzl/oracle-free:23-slim-faststart — smaller/faster than the official image and suitable for CI.
/// Grants EXECUTE ON DBMS_LOCK to the test user so DBMS_LOCK-based advisory locks work;
/// without this grant, calls surface as ORA-06550.
///
/// The SYSTEM/admin password is pinned explicitly via WithPassword so the admin-grant step
/// cannot silently break if the base image's default changes.
/// </summary>
public sealed class OracleFixture : IAsyncLifetime
{
    // gvenzl/oracle-free applies the same password to SYS, SYSTEM, and PDBADMIN.
    private const string AdminPassword = "oracle";

    private readonly OracleContainer _container = new OracleBuilder()
        .WithImage("gvenzl/oracle-free:23-slim-faststart")
        .WithPassword(AdminPassword)
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await GrantDbmsLockExecuteAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private async Task GrantDbmsLockExecuteAsync()
    {
        var appUser = ExtractUser(_container.GetConnectionString());
        var systemConnString = BuildSystemConnectionString(_container.GetConnectionString());

        await using var conn = new global::Oracle.ManagedDataAccess.Client.OracleConnection(systemConnString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"GRANT EXECUTE ON DBMS_LOCK TO \"{appUser}\"";
        await cmd.ExecuteNonQueryAsync();
    }

    private static string ExtractUser(string connectionString)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        return builder.TryGetValue("User Id", out var v) ? v?.ToString() ?? "" : "";
    }

    private static string BuildSystemConnectionString(string connectionString)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        builder["User Id"] = "SYSTEM";
        builder["Password"] = AdminPassword;
        return builder.ConnectionString;
    }
}
