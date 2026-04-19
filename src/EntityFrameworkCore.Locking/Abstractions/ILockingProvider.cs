namespace EntityFrameworkCore.Locking.Abstractions;

/// <summary>
/// Root provider abstraction. Phase 1 exposes only row-level locking.
/// Phase 2 adds advisory locks by implementing AdvisoryLockProvider — non-breaking via C# DIM.
/// </summary>
public interface ILockingProvider
{
    /// <summary>Row-level lock SQL generator for this provider.</summary>
    ILockSqlGenerator RowLockGenerator { get; }

    /// <summary>Provider name for diagnostics (e.g., "PostgreSQL", "MySQL", "SqlServer").</summary>
    string ProviderName { get; }

    /// <summary>Translates provider-specific database exceptions to typed locking exceptions.</summary>
    IExceptionTranslator ExceptionTranslator { get; }

    /// <summary>
    /// Phase 2 seam: advisory lock provider. Returns null in Phase 1.
    /// Phase 2 providers override this default interface member.
    /// </summary>
    IAdvisoryLockProvider? AdvisoryLockProvider => null;
}
