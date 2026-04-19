namespace EntityFrameworkCore.Locking.Internal;

internal static class LockTagConstants
{
    internal const string Prefix = "__efcore_locking:";

    internal static string BuildTag(LockOptions options) =>
        FormattableString.Invariant($"{Prefix}{options.Mode}:{options.Behavior}:{options.Timeout?.TotalMilliseconds}");
}
