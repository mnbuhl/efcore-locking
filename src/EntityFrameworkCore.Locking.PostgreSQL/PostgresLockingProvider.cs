using EntityFrameworkCore.Locking.Abstractions;

namespace EntityFrameworkCore.Locking.PostgreSQL;

internal sealed class PostgresLockingProvider : ILockingProvider
{
    public ILockSqlGenerator RowLockGenerator { get; } = new PostgresLockSqlGenerator();
    public string ProviderName => "PostgreSQL";

    public Task ValidateProviderAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask; // PostgreSQL supports all lock modes; no version constraint
}
