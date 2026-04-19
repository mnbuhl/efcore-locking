namespace EntityFrameworkCore.Locking.Exceptions;

/// <summary>
/// Thrown for programmer errors: missing transaction, unsupported query shape,
/// unsupported provider version, or unsupported lock mode combination.
/// </summary>
public class LockingConfigurationException : LockingException
{
    public LockingConfigurationException(string message)
        : base(message) { }

    public LockingConfigurationException(string message, Exception innerException)
        : base(message, innerException) { }
}
