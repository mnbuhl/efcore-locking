# EntityFrameworkCore.Locking

Pessimistic locking for EF Core. Supports PostgreSQL, MySQL, and SQL Server.

- **Row-level locks** — `ForUpdate()` / `ForShare()` LINQ extensions, scoped to a transaction
- **Distributed locks** — `AcquireDistributedLockAsync()` session-scoped advisory locks, no transaction required
- **Transaction distributed locks** — `AcquireDistributedLockAsync(..., scope: DistributedLockScope.Transaction)` native advisory locks owned by an existing transaction (PostgreSQL and SQL Server)

## Installation

```
dotnet add package EntityFrameworkCore.Locking.PostgreSQL
dotnet add package EntityFrameworkCore.Locking.MySql
dotnet add package EntityFrameworkCore.Locking.SqlServer
```

## Setup

Call `.UseLocking()` after your provider's `Use*()` call:

```csharp
// PostgreSQL
services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(connectionString)
     .UseLocking());

// MySQL
services.AddDbContext<AppDbContext>(o =>
    o.UseMySql(connectionString, serverVersion)
     .UseLocking());

// SQL Server
services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(connectionString)
     .UseLocking());
```

## Row-level locks

All row-level locking queries **require an active transaction**.

```csharp
await using var tx = await ctx.Database.BeginTransactionAsync();

// Exclusive lock (FOR UPDATE / WITH (UPDLOCK, HOLDLOCK, ROWLOCK))
var product = await ctx.Products
    .Where(p => p.Id == id)
    .ForUpdate()
    .FirstOrDefaultAsync();

// Skip rows already locked by another transaction
var available = await ctx.Products
    .Where(p => p.Status == "pending")
    .ForUpdate(LockBehavior.SkipLocked)
    .ToListAsync();

// Fail immediately if the lock cannot be acquired
var row = await ctx.Products
    .Where(p => p.Id == id)
    .ForUpdate(LockBehavior.NoWait)
    .FirstOrDefaultAsync();

// Wait up to 500ms for the lock
var row = await ctx.Products
    .Where(p => p.Id == id)
    .ForUpdate(LockBehavior.Wait, TimeSpan.FromMilliseconds(500))
    .FirstOrDefaultAsync();

// Shared lock (PostgreSQL and MySQL only)
var row = await ctx.Products
    .Where(p => p.Id == id)
    .ForShare()
    .FirstOrDefaultAsync();

await tx.CommitAsync();
```

### PostgreSQL-only modes

Available when referencing `EntityFrameworkCore.Locking.PostgreSQL`:

```csharp
// FOR NO KEY UPDATE — blocks writers but allows FOR KEY SHARE (FK lookups)
await ctx.Products.Where(p => p.Id == id).ForNoKeyUpdate().FirstOrDefaultAsync();

// FOR KEY SHARE — minimal shared lock, only blocks FOR UPDATE
await ctx.Products.Where(p => p.Id == id).ForKeyShare().FirstOrDefaultAsync();
```

### Include with locking

On PostgreSQL, collection `Include` emits `FOR UPDATE OF "t"` automatically to handle the outer join, so you can use `Include` directly without `AsSplitQuery()`:

```csharp
var product = await ctx.Products
    .Include(p => p.OrderLines)
    .Where(p => p.Id == id)
    .ForUpdate()
    .FirstOrDefaultAsync();
```

### Queue processing

`ForUpdate(LockBehavior.SkipLocked)` is the standard building block for a worker queue where multiple consumers race to claim items:

```csharp
await using var tx = await ctx.Database.BeginTransactionAsync();

var item = await ctx.Jobs
    .Where(j => j.Status == "pending")
    .OrderBy(j => j.CreatedAt)
    .ForUpdate(LockBehavior.SkipLocked)
    .FirstOrDefaultAsync();

if (item is null)
    return; // all items claimed by other workers

item.Status = "processing";
await ctx.SaveChangesAsync();
await tx.CommitAsync();
```

### Lock modes and behaviors

| Method | Generated SQL |
|--------|--------------|
| `ForUpdate()` | `FOR UPDATE` / `WITH (UPDLOCK, HOLDLOCK, ROWLOCK)` |
| `ForUpdate(LockBehavior.NoWait)` | `FOR UPDATE NOWAIT` / `SET LOCK_TIMEOUT 0` |
| `ForUpdate(LockBehavior.SkipLocked)` | `FOR UPDATE SKIP LOCKED` (PG/MySQL) / `WITH (UPDLOCK, ROWLOCK, READPAST)` (SQL Server) |
| `ForUpdate(LockBehavior.Wait, timeout)` | `SET LOCAL lock_timeout` (PG) / `SET SESSION innodb_lock_wait_timeout` (MySQL) / `SET LOCK_TIMEOUT` (SQL Server) |
| `ForShare()` | `FOR SHARE` (PostgreSQL/MySQL only) |
| `ForNoKeyUpdate()` | `FOR NO KEY UPDATE` (PostgreSQL only) |
| `ForKeyShare()` | `FOR KEY SHARE` (PostgreSQL only) |

## Distributed locks

Advisory locks coordinate across processes using an application-defined key. The following methods default to session scope — held until the handle is disposed or the connection drops. Pass `scope: DistributedLockScope.Transaction` to hold the lock until transaction completion, as described below.

```csharp
// Blocks until available
await using var handle = await ctx.Database.AcquireDistributedLockAsync("invoice:generate");

// With a timeout — throws LockTimeoutException if not acquired in time
await using var handle = await ctx.Database.AcquireDistributedLockAsync(
    "report:daily", TimeSpan.FromSeconds(5));

// With cancellation
await using var handle = await ctx.Database.AcquireDistributedLockAsync(
    "report:daily", timeout: null, ct: ct);

// Shared lock where supported
await using var handle = await ctx.Database.AcquireDistributedLockAsync(
    "catalog:read", mode: DistributedLockMode.Shared);

// Non-blocking — returns null immediately if held
var handle = await ctx.Database.TryAcquireDistributedLockAsync("invoice:generate");
if (handle is null)
    return Results.Conflict("Another process is generating the invoice.");
await using (handle) { /* critical section */ }

// Synchronous variants
using var handle = ctx.Database.AcquireDistributedLock("report:daily");
var handle = ctx.Database.TryAcquireDistributedLock("report:daily");

// Runtime check
if (ctx.Database.SupportsDistributedLocks()) { ... }
```

### Transaction-scoped distributed locks

On PostgreSQL and SQL Server, pass `scope: DistributedLockScope.Transaction` to the same distributed-lock methods when a lock must protect a read-check-write sequence through commit or rollback. The connection must already be open and have an active EF transaction, including an external transaction attached through `UseTransaction`. In transaction scope, the methods do not open/close connections or create/enlist transactions. They return acquisition results through `IDistributedLockHandle`; release and disposal are no-ops because the transaction owns the lock lifetime. Omitting `scope` preserves session behavior, even when a transaction exists.

```csharp
await using var tx = await ctx.Database.BeginTransactionAsync();

await using var result = await ctx.Database.AcquireDistributedLockAsync(
    "stock:123", TimeSpan.FromSeconds(5), scope: DistributedLockScope.Transaction);

// Read, check, and write while holding the advisory lock.
await ctx.SaveChangesAsync();
await tx.CommitAsync(); // the database releases the lock
```

Non-blocking acquisition returns an immutable result object:

```csharp
await using var tx = await ctx.Database.BeginTransactionAsync();
await using var result = await ctx.Database.TryAcquireDistributedLockAsync(
    "stock:123", scope: DistributedLockScope.Transaction);
if (result is not { IsAcquired: true })
    return; // ordinary contention; transaction disposal rolls back

// Perform the protected operation.
await tx.CommitAsync();
// result.IsAcquired remains true: it records the attempt, not current lock ownership.
```

Synchronous variants are `AcquireDistributedLock()` and `TryAcquireDistributedLock()`, with the same `scope` argument. `TryAcquire` in transaction scope always returns an immutable result, with `IsAcquired=false` for ordinary contention; its return type remains nullable because session scope still returns null on contention. Both sync/async variants accept `DistributedLockMode.Shared` or `Exclusive` (default). `SupportsTransactionDistributedLocks()` reports provider capability, independent of whether the connection or transaction is ready. Reacquiring a key in the same transaction follows the database's native behavior, including contention when strengthening shared ownership to exclusive.

For `TransactionScope`, explicitly enlist through EF after opening the connection. Driver auto-enlistment alone does not populate EF's tracked enlistment and is rejected by the transaction lock API. Enable async flow and keep connection cleanup outside the completed scope:

```csharp
using System.Transactions;

try
{
    using (var scope = new TransactionScope(
        TransactionScopeOption.Required,
        new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
        TransactionScopeAsyncFlowOption.Enabled))
    {
        await ctx.Database.OpenConnectionAsync();
        ctx.Database.EnlistTransaction(Transaction.Current);
        await using var result = await ctx.Database.AcquireDistributedLockAsync(
            "stock:123", scope: DistributedLockScope.Transaction);

        await ctx.SaveChangesAsync();
        scope.Complete(); // commit occurs when the owning scope is disposed
    }
}
finally
{
    await ctx.Database.CloseConnectionAsync();
}
```

| Provider | Native transaction lock | Supported |
|----------|-------------------------|-----------|
| PostgreSQL | `pg_advisory_xact_lock` / `pg_advisory_xact_lock_shared` | Yes |
| SQL Server | `sp_getapplock @LockOwner='Transaction'` | Yes |
| MySQL | No native equivalent | No; acquisition throws `LockingConfigurationException` |

The existing key encoding is shared with session locks, so conflicting session and transaction requests on the same key contend across connections. Session locking remains available on all three providers.

A null timeout adds no library wait limit, zero performs one no-wait attempt (and `Acquire` throws `LockTimeoutException` on contention), and positive durations round up to milliseconds and clamp to the native signed-int millisecond range. Negative durations are configuration errors. The native acquisition command has no driver command-timeout limit; cancellation is best-effort, and a caller/server policy such as PostgreSQL `statement_timeout` or `lock_timeout` can still impose a shorter wait.

After any acquisition failure, roll back the affected transaction or exit its scope without completing it. PostgreSQL timeout/cancellation/deadlock errors can abort the transaction; a cancellation before dispatch can leave it usable but with a temporary local timeout. Successful finite-timeout acquisition restores the caller's original `lock_timeout`. The library neither issues recovery SQL after failure nor creates savepoints. SQL Server application-lock deadlock return codes do not themselves roll back the transaction.

Calling `Release`, `ReleaseAsync`, `Dispose`, or `DisposeAsync` on a transaction result does not release the lock or affect the connection or transaction. Native savepoint semantics still apply: on PostgreSQL, rolling back to a savepoint releases transaction locks acquired after that savepoint. An `IsAcquired` result continues to report the original successful attempt.

### Lock keys

Keys are plain strings, up to **255 characters**. Provider-specific encoding is handled internally:

- **PostgreSQL** — hashed to a `bigint` via XxHash32 with a namespace prefix (`"EFLK"`), computed in-process.
- **MySQL** — passed as-is for keys ≤ 64 UTF-8 bytes; longer keys are SHA-256 hashed to `lock:<hex58>`. The `lock:` prefix is reserved.
- **SQL Server** — passed as-is (max 255 chars, enforced upstream).

### Provider-specific behavior

| Feature | PostgreSQL | MySQL | SQL Server |
|---------|-----------|-------|-----------|
| Native primitive | `pg_advisory_lock` | `GET_LOCK` | `sp_getapplock @LockOwner='Session'` |
| Shared distributed locks | `pg_advisory_lock_shared` | Not supported by `GET_LOCK` | `sp_getapplock @LockMode='Shared'` |
| Timeout | `SET LOCAL lock_timeout` (ms) | `GET_LOCK(@key, seconds)` — rounded up to 1 s | `@LockTimeout` ms |
| Cancellation | Driver-level (best-effort) | `KILL QUERY` side-channel | Attention signal |

MySQL distributed locks use `GET_LOCK`, which is exclusive-only. Requesting `DistributedLockMode.Shared` on MySQL throws `LockingConfigurationException`.

Advisory lock SQL is a blocking database call. Cancellation is a best-effort signal to the driver; if the driver does not honor it before the timeout fires, the call completes via timeout. Combine a `timeout` with the `CancellationToken` for bounded waits.

## Exceptions

```csharp
try
{
    var row = await ctx.Products
        .Where(p => p.Id == id)
        .ForUpdate(LockBehavior.NoWait)
        .FirstOrDefaultAsync();
}
catch (LockTimeoutException)
{
    // NOWAIT failed or timeout exceeded
}
catch (DeadlockException)
{
    // Deadlock detected — retry the transaction
}
catch (LockingConfigurationException)
{
    // Programmer error: missing transaction, unsupported query shape,
    // or provider does not support the requested mode.
}
```

`LockAlreadyHeldException` is thrown synchronously before any database call when the same `(DbContext, connection, key)` triple is already registered. Acquiring the same key from two **different** `DbContext` instances on different connections will block (or return `null` for `TryAcquire`) as expected.

**Exception hierarchy:**

- `LockingException`
  - `LockAcquisitionFailedException`
    - `LockTimeoutException`
    - `DeadlockException`
    - `LockAlreadyHeldException`
  - `LockingConfigurationException`

## Provider support

| Feature | PostgreSQL | MySQL | SQL Server |
|---------|-----------|-------|-----------|
| `ForUpdate` | ✓ | ✓ | ✓ |
| `ForShare` | ✓ | ✓ | ✗ |
| `ForNoKeyUpdate` | ✓ | ✗ | ✗ |
| `ForKeyShare` | ✓ | ✗ | ✗ |
| `SkipLocked` | ✓ | ✓ | ✓ (via `READPAST`) |
| `NoWait` | ✓ | ✓ | ✓ |
| Wait with timeout | ✓ (ms) | ✓ (ceil to 1s) | ✓ (ms) |

SQL Server uses `WITH (UPDLOCK, ROWLOCK, READPAST)` for `SkipLocked` — `READPAST` skips rows held under row-level or page-level locks, but rows under a table-level lock are blocked rather than skipped.

MySQL's `innodb_lock_wait_timeout` is in whole seconds, so sub-second timeouts are rounded up to 1 second.

## Unsupported query shapes

The following query shapes throw `LockingConfigurationException` at execution time:

- `UNION` / `EXCEPT` / `INTERSECT` / `CONCAT` — lock individual queries before combining results
- `AsSplitQuery()` — use regular `Include()` instead (PostgreSQL emits `FOR UPDATE OF` automatically for outer joins)
- `Distinct()` — not compatible with row-level locking on any supported database
- `CountAsync()` / `LongCountAsync()` / `SumAsync()` / `MaxAsync()` / `MinAsync()` — aggregate terminal operations are rejected because the result is a scalar, not a set of lockable rows; use `AnyAsync()` if you want to test for row existence with a lock

Explicit joins (LINQ `join` syntax, `SelectMany`), correlated subqueries (`Any`, `Contains`), `Where`+`OrderBy`+`Take` pagination, and all `Include` / `ThenInclude` shapes work correctly across all providers.

## Limitations

The following scenarios are not detected at build or execution time:

| Scenario | Behaviour | Notes |
|---|---|---|
| `FromSqlRaw` / `FromSql` + `ForUpdate()` | Lock clause appended to the wrapping `SELECT` — works in most cases; may fail if the raw SQL shape prevents composing a valid outer query | Test your specific query |
| `EF.CompileAsyncQuery` + `ForUpdate()` | **Throws at compile time.** `ForUpdate` is not a translatable LINQ expression and cannot be used inside `EF.CompileAsyncQuery`. | Architectural constraint of EF Core compiled queries |
| `ExecuteUpdate` / `ExecuteDelete` / `Database.ExecuteSqlRaw` | Locking has no effect — these bypass the query SQL generator | Use `ForUpdate()` only with `IQueryable<T>` |
| SQL Server nested subqueries | Table hints (`WITH (UPDLOCK, HOLDLOCK, ROWLOCK)`) are applied to all `TableExpression` nodes in the locking `SELECT`, including correlated subqueries | SQL Server requires per-table hints; subquery coverage is correct and intentional |
| GroupBy + ForUpdate | No compile-time error; the lock clause is applied to the outer `SELECT` that wraps EF Core's subquery translation, so the lock targets the grouping result rows rather than individual base-table rows — semantics may not be what you expect | |

## Supported database versions

| Database | Minimum | Notes |
|----------|---------|-------|
| PostgreSQL | 14 | Default minimum for Npgsql 8.x. PG 12+ works if you call `.SetPostgresVersion(12, 0)` in `UseNpgsql`. |
| MySQL | 8.0 | `FOR SHARE`, `SKIP LOCKED`, and `NOWAIT` were introduced in 8.0.1. MySQL 5.7 is not supported. |
| MariaDB | 10.6 | `SKIP LOCKED` requires 10.6+. `NOWAIT` requires 10.3+. `ForShare` emits `LOCK IN SHARE MODE`. |
| SQL Server | 2019 | Azure SQL Database is also supported. |

## Target frameworks

`net8.0`, `net9.0`, `net10.0`

## Benchmarks

The `benchmarks/` directory contains BenchmarkDotNet benchmarks measuring the overhead added by the SQL generator and interceptor.

```bash
dotnet run -c Release --project benchmarks/EntityFrameworkCore.Locking.Benchmarks -- --version=<x.y.z>
```

`--version` is required and labels the results folder (`results/v<x.y.z>/`). Additional BenchmarkDotNet arguments (e.g. `--filter '*SqlGeneration*'`) can be appended.

## License

[MIT](LICENSE)
