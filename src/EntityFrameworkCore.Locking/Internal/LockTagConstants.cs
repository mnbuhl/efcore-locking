namespace EntityFrameworkCore.Locking.Internal;

internal static class LockTagConstants
{
    internal const string Prefix = "__efcore_locking:";

    /// <summary>
    /// Finds a lock tag in leading query comments, avoiding the body of ordinary SELECT queries.
    /// </summary>
    internal static int FindTag(string commandText)
    {
        var offset = 0;
        while (offset < commandText.Length)
        {
            while (offset < commandText.Length && char.IsWhiteSpace(commandText[offset]))
                offset++;

            var remaining = commandText.AsSpan(offset);
            if (!remaining.StartsWith("--", StringComparison.Ordinal))
            {
                // EF read queries put their tags before SELECT. Only skip the body
                // for that known layout; arbitrary preambles (DECLARE, EXEC, SET,
                // block comments, etc.) must retain the original full-text search.
                // Check the token boundary so SELECTED/SELECT_value are not shortcuts.
                if (
                    remaining.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                    && (remaining.Length == 6 || char.IsWhiteSpace(remaining[6]))
                )
                    return -1;

                return commandText.IndexOf(Prefix, offset, StringComparison.Ordinal);
            }

            offset += 2;
            remaining = commandText.AsSpan(offset);
            // EF emits "-- " followed by the tag. Avoid finding the line ending first
            // for the common locking-query path.
            if (remaining.StartsWith(" " + Prefix, StringComparison.Ordinal))
                return offset + 1;

            var lineEnd = remaining.IndexOf('\n');
            var line = lineEnd < 0 ? remaining : remaining[..lineEnd];
            var prefixIndex = line.IndexOf(Prefix, StringComparison.Ordinal);
            if (prefixIndex >= 0)
                return offset + prefixIndex;
            if (lineEnd < 0)
                return -1;

            offset += lineEnd + 1;
        }

        return -1;
    }

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
        if (parts[2].Length > 0)
        {
            if (
                !double.TryParse(
                    parts[2],
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var ms
                )
                || !double.IsFinite(ms)
                || ms < 0
            )
                return false;

            timeout = TimeSpan.FromMilliseconds(ms);
        }

        options = new LockOptions
        {
            Mode = mode,
            Behavior = behavior,
            Timeout = timeout,
        };
        return true;
    }
}
