# EntityFrameworkCore.Locking

Pessimistic locking for EF Core. Supports PostgreSQL, MySQL, and SQL Server.

- **Row-level locks** — `ForUpdate()` / `ForShare()` LINQ extensions, scoped to a transaction
- **Distributed locks** — `AcquireDistributedLockAsync()` session-scoped advisory locks, no transaction required

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

Advisory locks coordinate across processes without tying the lock to a row or transaction. They are session-scoped — held until the handle is disposed or the connection drops.

```csharp
// Blocks until available
await using var handle = await ctx.Database.AcquireDistributedLockAsync("invoice:generate");

// With a timeout — throws LockTimeoutException if not acquired in time
await using var handle = await ctx.Database.AcquireDistributedLockAsync(
    "report:daily", TimeSpan.FromSeconds(5));

// With cancellation
await using var handle = await ctx.Database.AcquireDistributedLockAsync(
    "report:daily", timeout: null, cancellationToken: ct);

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

### Lock keys

Keys are plain strings, up to **255 characters**. Provider-specific encoding is handled internally:

- **PostgreSQL** — hashed to a `bigint` via XxHash32 with a namespace prefix (`"EFLK"`), computed in-process.
- **MySQL** — passed as-is for keys ≤ 64 UTF-8 bytes; longer keys are SHA-256 hashed to `lock:<hex58>`. The `lock:` prefix is reserved.
- **SQL Server** — passed as-is (max 255 chars, enforced upstream).

### Provider-specific behavior

| Feature | PostgreSQL | MySQL | SQL Server |
|---------|-----------|-------|-----------|
| Native primitive | `pg_advisory_lock` | `GET_LOCK` | `sp_getapplock @LockOwner='Session'` |
| Timeout | `SET LOCAL lock_timeout` (ms) | `GET_LOCK(@key, seconds)` — rounded up to 1 s | `@LockTimeout` ms |
| Cancellation | Driver-level (best-effort) | `KILL QUERY` side-channel | Attention signal |

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

- `UNION` / `EXCEPT` / `INTERSECT` — lock individual queries before combining results
- `AsSplitQuery()` — use regular `Include()` instead (PostgreSQL emits `FOR UPDATE OF` automatically for outer joins)
- `Distinct()` — not compatible with row-level locking on any supported database
- `GroupBy(...)` — not compatible with row-level locking on any supported database

## Limitations

The following scenarios are not detected at build or execution time:

| Scenario | Behaviour | Notes |
|---|---|---|
| `FromSqlRaw` / `FromSqlInterpolated` + `ForUpdate()` | Lock clause appended to wrapping `SELECT` — may work or fail depending on user SQL shape | Test your specific query |
| `EF.CompileAsyncQuery` + `ForUpdate()` | Lock clause emitted correctly; pre-statement timeout SQL not injected | Architectural constraint of EF Core compiled queries |
| `ExecuteUpdate` / `ExecuteDelete` / `Database.ExecuteSqlRaw` | Locking has no effect — these bypass the query SQL generator | Use `ForUpdate()` only with `IQueryable<T>` |
| SQL Server nested subqueries | Table hints applied to all `TableExpression` nodes in the locking SELECT, including correlated subqueries | SQL Server requires per-table hints; this is correct behaviour |

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
