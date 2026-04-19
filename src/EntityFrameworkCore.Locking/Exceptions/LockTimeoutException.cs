namespace EntityFrameworkCore.Locking.Exceptions;

/// <summary>Thrown when a lock acquisition times out (NOWAIT or WAIT timeout exceeded).</summary>
public class LockTimeoutException : LockAcquisitionFailedException
{
    public LockTimeoutException(string message)
        : base(message) { }

    public LockTimeoutException(string message, Exception innerException)
        : base(message, innerException) { }
}
