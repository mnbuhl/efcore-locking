namespace EntityFrameworkCore.Locking.Internal;

/// <summary>
/// Ambient lock options flowing from ForUpdate()/ForShare() call site to SQL generation.
/// AsyncLocal is per-async-execution-context, so it is naturally scoped to the query's await chain.
/// </summary>
internal static class LockContext
{
    private static readonly AsyncLocal<LockOptions?> _current = new();

    internal static LockOptions? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
