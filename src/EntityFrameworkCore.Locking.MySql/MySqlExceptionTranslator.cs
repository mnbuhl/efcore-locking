using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using MySqlConnector;

namespace EntityFrameworkCore.Locking.MySql;

public sealed class MySqlExceptionTranslator : IExceptionTranslator
{
    public LockingException? Translate(Exception exception)
    {
        var mysqlEx = exception as MySqlException ?? exception.InnerException as MySqlException;

        if (mysqlEx is null)
            return null;

        return mysqlEx.Number switch
        {
            1213 => new DeadlockException("MySQL deadlock detected.", mysqlEx),
            1205 => new LockTimeoutException("MySQL lock wait timeout exceeded.", mysqlEx),
            // 3572: NOWAIT locking failure ("Statement aborted because lock(s) could not be acquired immediately")
            3572 => new LockTimeoutException(
                "MySQL lock could not be acquired immediately (NOWAIT).",
                mysqlEx
            ),
            _ => null,
        };
    }
}
