namespace EntityFrameworkCore.Locking.Exceptions;

/// <summary>Base class for all exceptions thrown by EntityFrameworkCore.Locking.</summary>
public class LockingException : Exception
{
    public LockingException(string message)
        : base(message) { }

    public LockingException(string message, Exception innerException)
        : base(message, innerException) { }
}
