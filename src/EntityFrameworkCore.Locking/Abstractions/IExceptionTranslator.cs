using EntityFrameworkCore.Locking.Exceptions;

namespace EntityFrameworkCore.Locking.Abstractions;

/// <summary>Translates provider-specific DbExceptions into typed LockingExceptions.</summary>
public interface IExceptionTranslator
{
    /// <summary>
    /// Returns a typed <see cref="LockingException"/> for the given exception, or null
    /// if the exception is not a recognized locking error for this provider.
    /// </summary>
    LockingException? Translate(Exception exception);
}
