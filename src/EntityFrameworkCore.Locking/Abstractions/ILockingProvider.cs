namespace EntityFrameworkCore.Locking.Abstractions;

/// <summary>Root provider abstraction for the locking library.</summary>
public interface ILockingProvider
{
    /// <summary>Row-level lock SQL generator for this provider.</summary>
    ILockSqlGenerator RowLockGenerator { get; }

    /// <summary>Provider name for diagnostics (e.g., "PostgreSQL", "MySQL", "SqlServer").</summary>
    string ProviderName { get; }

    /// <summary>Translates provider-specific database exceptions to typed locking exceptions.</summary>
    IExceptionTranslator ExceptionTranslator { get; }

    /// <summary>Advisory lock provider, or null if the provider does not support distributed locks.</summary>
    IAdvisoryLockProvider? AdvisoryLockProvider => null;
}
