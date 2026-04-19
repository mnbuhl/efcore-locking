namespace EntityFrameworkCore.Locking.Exceptions;

/// <summary>Thrown when the caller attempts to acquire a lock that is already held on the same connection.</summary>
public class LockAlreadyHeldException : LockingException
{
    public string Key { get; }

    public LockAlreadyHeldException(string key)
        : base($"A distributed lock for key '{key}' is already held on this connection. Nested acquisition of the same key is not supported.")
    {
        Key = key;
    }

    public LockAlreadyHeldException(string key, Exception innerException)
        : base($"A distributed lock for key '{key}' is already held on this connection. Nested acquisition of the same key is not supported.", innerException)
    {
        Key = key;
    }
}
