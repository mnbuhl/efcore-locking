namespace EntityFrameworkCore.Locking.Exceptions;

/// <summary>Thrown when a row-level lock cannot be acquired from the database.</summary>
public class LockAcquisitionFailedException : LockingException
{
    public LockAcquisitionFailedException(string message) : base(message) { }
    public LockAcquisitionFailedException(string message, Exception innerException) : base(message, innerException) { }
}
