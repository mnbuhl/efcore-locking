using EntityFrameworkCore.Locking.Abstractions;

namespace EntityFrameworkCore.Locking.SqlServer;

internal sealed class SqlServerLockingProvider : ILockingProvider
{
    public ILockSqlGenerator RowLockGenerator { get; } = new SqlServerLockSqlGenerator();
    public IExceptionTranslator ExceptionTranslator { get; } = new SqlServerExceptionTranslator();
    public string ProviderName => "SqlServer";

    public Task ValidateProviderAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
