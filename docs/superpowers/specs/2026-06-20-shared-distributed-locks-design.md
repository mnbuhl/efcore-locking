# Shared distributed locks

## Context

GitHub issue #14 asks for shared distributed locks, originally for PostgreSQL. The library currently exposes session-scoped advisory locks through `DatabaseFacade` extension methods and provider-specific `IAdvisoryLockProvider` implementations for PostgreSQL, MySQL, and SQL Server.

The current public distributed lock API supports only exclusive locks. Row-level locking already has shared modes, but those are transaction-scoped query locks and are separate from distributed advisory locks.

Provider capability is not uniform:

- PostgreSQL supports shared session advisory locks through `pg_advisory_lock_shared`, `pg_try_advisory_lock_shared`, and `pg_advisory_unlock_shared`.
- SQL Server supports shared application locks through `sp_getapplock @LockMode = 'Shared'`.
- MySQL's current provider uses `GET_LOCK`, which is exclusive-only. MySQL has a separate locking service with read/write concepts, but that is not the primitive this provider currently uses.

## Decision

Add mode-aware distributed lock acquisition directly to the existing public methods by adding a `DistributedLockMode mode = DistributedLockMode.Exclusive` parameter.

This intentionally prioritizes a small API over binary compatibility with already-compiled consumers. Existing source calls remain source-compatible because the new parameter defaults to `Exclusive`.

Rejected alternatives:

- Preserve old signatures and add overloads. This keeps binary compatibility but adds API clutter for a small consumer base.
- Add shared-specific methods such as `AcquireSharedDistributedLockAsync`. This is readable but duplicates the acquisition API surface and makes future modes harder to express.

## Public API

Add a new enum in the core package:

```csharp
public enum DistributedLockMode
{
    Exclusive = 0,
    Shared = 1,
}
```

Update distributed lock methods so the mode is an optional parameter:

```csharp
Task<IDistributedLockHandle> AcquireDistributedLockAsync(
    this DatabaseFacade database,
    string key,
    TimeSpan? timeout = null,
    CancellationToken ct = default,
    DistributedLockMode mode = DistributedLockMode.Exclusive);

Task<IDistributedLockHandle?> TryAcquireDistributedLockAsync(
    this DatabaseFacade database,
    string key,
    CancellationToken ct = default,
    DistributedLockMode mode = DistributedLockMode.Exclusive);

IDistributedLockHandle AcquireDistributedLock(
    this DatabaseFacade database,
    string key,
    TimeSpan? timeout = null,
    DistributedLockMode mode = DistributedLockMode.Exclusive);

IDistributedLockHandle? TryAcquireDistributedLock(
    this DatabaseFacade database,
    string key,
    DistributedLockMode mode = DistributedLockMode.Exclusive);
```

Update `IAdvisoryLockProvider` methods to accept the selected `DistributedLockMode`.

## Provider Behavior

PostgreSQL:

- `Exclusive` uses the current `pg_advisory_lock`, `pg_try_advisory_lock`, and `pg_advisory_unlock` flow.
- `Shared` uses `pg_advisory_lock_shared`, `pg_try_advisory_lock_shared`, and `pg_advisory_unlock_shared`.
- Timeout and cancellation behavior stays unchanged.

SQL Server:

- `Exclusive` maps to `sp_getapplock @LockMode = 'Exclusive'`.
- `Shared` maps to `sp_getapplock @LockMode = 'Shared'`.
- Release still uses `sp_releaseapplock`; SQL Server releases the held application lock for the resource and owner.
- Existing return-code translation remains unchanged.

MySQL:

- `Exclusive` keeps the current `GET_LOCK` / `RELEASE_LOCK` behavior.
- `Shared` throws `LockingConfigurationException` before issuing SQL.
- The error message must state that MySQL's current `GET_LOCK` implementation does not support shared distributed locks.

## Registry Semantics

Keep the existing same-context guard unchanged: the same `(DbContext, DbConnection, key)` cannot be acquired twice, regardless of mode.

This means a context cannot hold both a shared and exclusive distributed lock for the same key at the same time. The rule is simpler than provider-specific reentrancy behavior and preserves the current `LockAlreadyHeldException` semantics.

## Error Handling

- Invalid keys still throw `LockingConfigurationException`.
- Unsupported shared mode on MySQL throws `LockingConfigurationException`.
- Contested non-blocking acquisition still returns `null`.
- Timed acquisition still throws `LockTimeoutException`.
- Existing provider exception translation behavior is unchanged.

## Documentation

Update the README distributed locks section with:

- The new `DistributedLockMode.Shared` usage.
- A provider support table for exclusive and shared distributed locks.
- A note that MySQL distributed locks remain exclusive-only with the current `GET_LOCK` provider.

## Testing

Core unit tests:

- Existing calls without a mode still request `Exclusive`.
- Explicit `Shared` is passed to the advisory provider.
- Same-context double acquire of the same key still throws `LockAlreadyHeldException`, regardless of mode.

PostgreSQL integration tests:

- Two contexts can hold `Shared` on the same key concurrently.
- `TryAcquireDistributedLockAsync(..., Shared)` returns `null` while an exclusive lock is held.
- `TryAcquireDistributedLockAsync(..., Exclusive)` returns `null` while a shared lock is held.
- Disposing all shared handles allows exclusive acquisition.

SQL Server integration tests:

- Same behavioral coverage as PostgreSQL using `sp_getapplock` shared mode.

MySQL tests:

- `Shared` acquisition throws `LockingConfigurationException`.
- Existing exclusive behavior remains covered by the existing distributed lock tests.
