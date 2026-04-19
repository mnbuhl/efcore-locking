using System.Data.Common;
using System.Runtime.CompilerServices;
using EntityFrameworkCore.Locking.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Locking.Internal;

/// <summary>
/// Tracks which (DbContext, DbConnection, key) triples are currently locked.
/// Keyed on DbContext so entries are collected when the context is GC'd.
/// Inner dictionary is keyed on DbConnection because advisory locks live on the physical connection.
/// </summary>
internal static class DistributedLockRegistry
{
    private sealed class Entry
    {
        public readonly Dictionary<DbConnection, HashSet<string>> ByConnection = new();
        public readonly object Gate = new();
    }

    private static readonly ConditionalWeakTable<DbContext, Entry> _table = new();

    public static void ThrowIfHeld(DbContext ctx, DbConnection conn, string key)
    {
        var entry = _table.GetOrCreateValue(ctx);
        lock (entry.Gate)
        {
            if (entry.ByConnection.TryGetValue(conn, out var keys) && keys.Contains(key))
                throw new LockAlreadyHeldException(key);
        }
    }

    public static void Register(DbContext ctx, DbConnection conn, string key)
    {
        var entry = _table.GetOrCreateValue(ctx);
        lock (entry.Gate)
        {
            if (!entry.ByConnection.TryGetValue(conn, out var keys))
            {
                keys = new HashSet<string>(StringComparer.Ordinal);
                entry.ByConnection[conn] = keys;
            }
            keys.Add(key);
        }
    }

    public static void Unregister(DbContext ctx, DbConnection conn, string key)
    {
        if (!_table.TryGetValue(ctx, out var entry))
            return;
        lock (entry.Gate)
        {
            if (entry.ByConnection.TryGetValue(conn, out var keys))
            {
                keys.Remove(key);
                if (keys.Count == 0)
                    entry.ByConnection.Remove(conn);
            }
        }
    }

    /// <summary>Returns all keys held on a given connection for a given context. Used by cleanup interceptor.</summary>
    public static IReadOnlyList<string> GetHeldKeys(DbContext ctx, DbConnection conn)
    {
        if (!_table.TryGetValue(ctx, out var entry))
            return Array.Empty<string>();
        lock (entry.Gate)
        {
            if (entry.ByConnection.TryGetValue(conn, out var keys))
                return keys.ToArray();
            return Array.Empty<string>();
        }
    }
}
