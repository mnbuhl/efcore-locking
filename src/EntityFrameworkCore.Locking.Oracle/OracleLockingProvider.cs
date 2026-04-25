using EntityFrameworkCore.Locking.Abstractions;

namespace EntityFrameworkCore.Locking.Oracle;

internal sealed class OracleLockingProvider : ILockingProvider
{
    public ILockSqlGenerator RowLockGenerator { get; } = new OracleLockSqlGenerator();
    public IExceptionTranslator ExceptionTranslator { get; } = new OracleExceptionTranslator();
    public IAdvisoryLockProvider AdvisoryLockProvider { get; } = new OracleAdvisoryLockProvider();
    public string ProviderName => "Oracle";
}
