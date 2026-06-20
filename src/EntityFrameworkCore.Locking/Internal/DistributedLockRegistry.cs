using System.Data.Common;
using System.Runtime.CompilerServices;
using EntityFrameworkCore.Locking.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Locking.Internal;

/// <summary>
/// Tracks distributed locks held by DbContext and by physical connection.
/// Context entries preserve same-context duplicate detection and connection entries prevent
/// mixed-mode session lock promotion from leaking between contexts sharing a connection.
/// </summary>
internal static class DistributedLockRegistry
{
    private sealed class ContextEntry
    {
        public readonly Dictionary<DbConnection, Dictionary<string, DistributedLockMode>> ByConnection = new();
        public readonly object Gate = new();
    }

    private sealed class ConnectionEntry
    {
        public readonly Dictionary<string, ConnectionLockState> ByKey = new(StringComparer.Ordinal);
        public readonly object Gate = new();
    }

    private sealed class ConnectionLockState
    {
        public ConnectionLockState(DistributedLockMode mode)
        {
            Mode = mode;
            Count = 1;
        }

        public DistributedLockMode Mode { get; }
        public int Count { get; set; }
    }

    private static readonly ConditionalWeakTable<DbContext, ContextEntry> _contexts = new();
    private static readonly ConditionalWeakTable<DbConnection, ConnectionEntry> _connections = new();

    /// <summary>Atomically checks for and registers the key. Throws <see cref="LockAlreadyHeldException"/> if already held.</summary>
    public static void RegisterOrThrow(DbContext ctx, DbConnection conn, string key, DistributedLockMode mode)
    {
        var contextEntry = _contexts.GetOrCreateValue(ctx);
        var connectionEntry = _connections.GetOrCreateValue(conn);

        lock (connectionEntry.Gate)
        {
            lock (contextEntry.Gate)
            {
                if (contextEntry.ByConnection.TryGetValue(conn, out var contextLocks) && contextLocks.ContainsKey(key))
                {
                    throw new LockAlreadyHeldException(key);
                }

                if (connectionEntry.ByKey.TryGetValue(key, out var connectionLock) && connectionLock.Mode != mode)
                {
                    throw new LockAlreadyHeldException(key);
                }

                if (contextLocks is null)
                {
                    contextLocks = new Dictionary<string, DistributedLockMode>(StringComparer.Ordinal);
                    contextEntry.ByConnection[conn] = contextLocks;
                }

                contextLocks.Add(key, mode);
                if (connectionLock is null)
                    connectionEntry.ByKey[key] = new ConnectionLockState(mode);
                else
                    connectionLock.Count++;
            }
        }
    }

    public static void Unregister(DbContext ctx, DbConnection conn, string key)
    {
        if (!_contexts.TryGetValue(ctx, out var contextEntry))
            return;

        if (!_connections.TryGetValue(conn, out var connectionEntry))
        {
            lock (contextEntry.Gate)
            {
                RemoveContextLock(contextEntry, conn, key);
            }
            return;
        }

        lock (connectionEntry.Gate)
        {
            lock (contextEntry.Gate)
            {
                if (!RemoveContextLock(contextEntry, conn, key))
                    return;

                if (!connectionEntry.ByKey.TryGetValue(key, out var connectionLock))
                    return;

                connectionLock.Count--;
                if (connectionLock.Count == 0)
                    connectionEntry.ByKey.Remove(key);
            }
        }
    }

    /// <summary>Returns all keys held on a given connection for a given context. Used by cleanup interceptor.</summary>
    public static IReadOnlyList<string> GetHeldKeys(DbContext ctx, DbConnection conn)
    {
        if (!_contexts.TryGetValue(ctx, out var entry))
            return Array.Empty<string>();
        lock (entry.Gate)
        {
            if (entry.ByConnection.TryGetValue(conn, out var keys))
                return keys.Keys.ToArray();
            return Array.Empty<string>();
        }
    }

    private static bool RemoveContextLock(ContextEntry contextEntry, DbConnection conn, string key)
    {
        if (!contextEntry.ByConnection.TryGetValue(conn, out var keys))
            return false;

        var removed = keys.Remove(key);
        if (keys.Count == 0)
            contextEntry.ByConnection.Remove(conn);

        return removed;
    }
}
