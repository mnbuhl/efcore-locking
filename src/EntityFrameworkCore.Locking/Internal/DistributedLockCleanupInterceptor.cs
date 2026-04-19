using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EntityFrameworkCore.Locking.Internal;

/// <summary>
/// Wired via LockingOptionsExtension.ApplyServices — emits a warning and performs
/// best-effort release for any handles not disposed before the connection closes.
/// </summary>
internal sealed class DistributedLockCleanupInterceptor : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionClosing(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result
    )
    {
        Cleanup(connection, eventData);
        return result;
    }

    public override ValueTask<InterceptionResult> ConnectionClosingAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result
    )
    {
        Cleanup(connection, eventData);
        return ValueTask.FromResult(result);
    }

    private static void Cleanup(DbConnection connection, ConnectionEventData eventData)
    {
        if (eventData.Context is null)
            return;

        var heldKeys = DistributedLockRegistry.GetHeldKeys(eventData.Context, connection);
        if (heldKeys.Count == 0)
            return;

        foreach (var key in heldKeys)
        {
            DistributedLockRegistry.Unregister(eventData.Context, connection, key);
        }

        // The physical advisory lock will be released automatically when the server closes the session.
        // Log a diagnostic warning that caller forgot to dispose lock handles.
        System.Diagnostics.Debug.WriteLine(
            $"[EntityFrameworkCore.Locking] WARNING: {heldKeys.Count} distributed lock(s) were not released before connection close: [{string.Join(", ", heldKeys)}]. Always dispose IDistributedLockHandle before disposing DbContext."
        );
    }
}
