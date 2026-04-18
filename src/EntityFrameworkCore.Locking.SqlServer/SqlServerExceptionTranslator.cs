using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using Microsoft.Data.SqlClient;

namespace EntityFrameworkCore.Locking.SqlServer;

public sealed class SqlServerExceptionTranslator : IExceptionTranslator
{
    public LockingException? Translate(Exception exception)
    {
        var sqlEx = exception as SqlException
            ?? exception.InnerException as SqlException;

        if (sqlEx is null)
            return null;

        // SqlException can contain multiple errors; check the most severe (first)
        return sqlEx.Number switch
        {
            1205 => new DeadlockException("SQL Server deadlock detected.", sqlEx),
            1222 => new LockTimeoutException("SQL Server lock request timeout exceeded.", sqlEx),
            _ => null
        };
    }
}
