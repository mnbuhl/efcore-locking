namespace EntityFrameworkCore.Locking.Internal;

internal static class LockTagConstants
{
    internal const string Prefix = "__efcore_locking:";

    internal static string BuildTag(LockOptions options) =>
        FormattableString.Invariant($"{Prefix}{options.Mode}:{options.Behavior}:{options.Timeout?.TotalMilliseconds}");

    /// <summary>
    /// Parses a lock tag produced by <see cref="BuildTag"/> back into <see cref="LockOptions"/>.
    /// Format: __efcore_locking:{LockMode}:{LockBehavior}:{timeout_ms | empty}
    /// </summary>
    internal static bool TryParse(string tag, out LockOptions? options)
    {
        options = null;
        if (!tag.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var body = tag[Prefix.Length..];
        var parts = body.Split(':');
        if (parts.Length != 3)
            return false;

        if (!Enum.TryParse<LockMode>(parts[0], out var mode))
            return false;

        if (!Enum.TryParse<LockBehavior>(parts[1], out var behavior))
            return false;

        TimeSpan? timeout = null;
        if (
            parts[2].Length > 0
            && double.TryParse(
                parts[2],
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var ms
            )
        )
            timeout = TimeSpan.FromMilliseconds(ms);

        options = new LockOptions { Mode = mode, Behavior = behavior, Timeout = timeout };
        return true;
    }
}
