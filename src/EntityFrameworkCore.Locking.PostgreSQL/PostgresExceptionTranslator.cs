using EntityFrameworkCore.Locking.Abstractions;
using EntityFrameworkCore.Locking.Exceptions;
using Npgsql;

namespace EntityFrameworkCore.Locking.PostgreSQL;

/// <summary>Translates Npgsql exceptions to typed locking exceptions.</summary>
public sealed class PostgresExceptionTranslator : IExceptionTranslator
{
    public LockingException? Translate(Exception exception)
    {
        var pgEx =
            exception as PostgresException
            ?? (exception as Exception)?.InnerException as PostgresException;

        if (pgEx is null)
            return null;

        return pgEx.SqlState switch
        {
            "40P01" => new DeadlockException("Deadlock detected by PostgreSQL.", pgEx),
            "55P03" => new LockTimeoutException(
                "Lock not available: the row is locked by another transaction (NOWAIT or lock_timeout exceeded).",
                pgEx
            ),
            _ => null,
        };
    }
}
