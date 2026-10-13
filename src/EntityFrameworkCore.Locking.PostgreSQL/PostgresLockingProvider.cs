using EntityFrameworkCore.Locking.Abstractions;

namespace EntityFrameworkCore.Locking.PostgreSQL;

internal sealed class PostgresLockingProvider : ILockingProvider
{
    public ILockSqlGenerator RowLockGenerator { get; } = new PostgresLockSqlGenerator();
    public IExceptionTranslator ExceptionTranslator { get; } = new PostgresExceptionTranslator();
    public IAdvisoryLockProvider AdvisoryLockProvider { get; } = new PostgresAdvisoryLockProvider();
    public ITransactionAdvisoryLockProvider TransactionAdvisoryLockProvider { get; } =
        new PostgresTransactionAdvisoryLockProvider();
    public string ProviderName => "PostgreSQL";
}
