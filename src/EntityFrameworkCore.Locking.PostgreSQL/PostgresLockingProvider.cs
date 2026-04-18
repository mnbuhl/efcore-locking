using EntityFrameworkCore.Locking.Abstractions;

namespace EntityFrameworkCore.Locking.PostgreSQL;

internal sealed class PostgresLockingProvider : ILockingProvider
{
    public ILockSqlGenerator RowLockGenerator { get; } = new PostgresLockSqlGenerator();
    public IExceptionTranslator ExceptionTranslator { get; } = new PostgresExceptionTranslator();
    public string ProviderName => "PostgreSQL";

    public Task ValidateProviderAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
