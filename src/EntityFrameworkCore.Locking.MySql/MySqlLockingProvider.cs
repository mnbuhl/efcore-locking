using EntityFrameworkCore.Locking.Abstractions;

namespace EntityFrameworkCore.Locking.MySql;

internal sealed class MySqlLockingProvider : ILockingProvider
{
    public ILockSqlGenerator RowLockGenerator { get; } = new MySqlLockSqlGenerator();
    public IExceptionTranslator ExceptionTranslator { get; } = new MySqlExceptionTranslator();
    public string ProviderName => "MySQL";
    public IAdvisoryLockProvider? AdvisoryLockProvider { get; } = new MySqlAdvisoryLockProvider();

    public Task ValidateProviderAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
