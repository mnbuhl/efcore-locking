using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using Oracle.ManagedDataAccess.Client;

namespace EntityFrameworkCore.Locking.Oracle;

/// <summary>
/// Translates Oracle exceptions to typed locking exceptions.
/// ORA-00054 resource busy (NOWAIT or WAIT timeout) -> LockTimeoutException
/// ORA-30006 resource busy; acquire with WAIT timeout expired -> LockTimeoutException
/// ORA-00060 deadlock detected -> DeadlockException
/// ORA-06550/PLS-00201 identifier 'DBMS_LOCK' must be declared -> LockingConfigurationException
///   (DBMS_LOCK requires an explicit GRANT EXECUTE — not granted to PUBLIC by default).
/// </summary>
public sealed class OracleExceptionTranslator : IExceptionTranslator
{
    public LockingException? Translate(Exception exception)
    {
        var oraEx = exception as OracleException ?? exception.InnerException as OracleException;

        if (oraEx is null)
            return null;

        return oraEx.Number switch
        {
            54 => new LockTimeoutException("Oracle resource busy (NOWAIT specified or wait timeout expired).", oraEx),
            30006 => new LockTimeoutException("Oracle resource busy; WAIT timeout expired.", oraEx),
            60 => new DeadlockException("Oracle deadlock detected.", oraEx),
            6550 => new LockingConfigurationException(
                "Oracle PL/SQL compilation error. If this is from a DBMS_LOCK call, "
                    + "the database user needs EXECUTE privilege on DBMS_LOCK: "
                    + "GRANT EXECUTE ON DBMS_LOCK TO <user>;",
                oraEx
            ),
            _ => null,
        };
    }
}
