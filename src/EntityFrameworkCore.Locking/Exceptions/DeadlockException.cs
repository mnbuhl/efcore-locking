namespace EntityFrameworkCore.Locking.Exceptions;

/// <summary>Thrown when the database detects a deadlock involving the current transaction.</summary>
public class DeadlockException : LockAcquisitionFailedException
{
    public DeadlockException(string message) : base(message) { }
    public DeadlockException(string message, Exception innerException) : base(message, innerException) { }
}
