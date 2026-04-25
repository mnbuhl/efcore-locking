# EntityFrameworkCore.Locking

Pessimistic locking for EF Core. Supports PostgreSQL, MySQL, SQL Server, and Oracle.

- **Row-level locks** — `ForUpdate()` / `ForShare()` LINQ extension methods scoped to a transaction
- **Distributed locks** — `AcquireDistributedLockAsync()` session-scoped advisory locks, no transaction required

## Installation

```
dotnet add package EntityFrameworkCore.Locking.PostgreSQL
dotnet add package EntityFrameworkCore.Locking.MySql
dotnet add package EntityFrameworkCore.Locking.SqlServer
dotnet add package EntityFrameworkCore.Locking.Oracle
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

// Oracle
services.AddDbContext<AppDbContext>(o =>
    o.UseOracle(connectionString)
     .UseLocking());
```

## Usage

All locking queries **require an active transaction**.

```csharp
await using var tx = await ctx.Database.BeginTransactionAsync();

// Basic exclusive lock (FOR UPDATE / WITH (UPDLOCK, HOLDLOCK, ROWLOCK))
var product = await ctx.Products
    .Where(p => p.Id == id)
    .ForUpdate()
    .FirstOrDefaultAsync();

// Skip rows already locked by another transaction
var available = await ctx.Products
    .Where(p => p.Status == "pending")
    .ForUpdate(LockBehavior.SkipLocked)
    .ToListAsync();

// Fail immediately if lock cannot be acquired
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

### PostgreSQL-only: ForNoKeyUpdate and ForKeyShare

These modes are available when using the `EntityFrameworkCore.Locking.PostgreSQL` package:

```csharp
// FOR NO KEY UPDATE — blocks writers but allows FOR KEY SHARE (FK lookups)
var row = await ctx.Products
    .Where(p => p.Id == id)
    .ForNoKeyUpdate()
    .FirstOrDefaultAsync();

// FOR KEY SHARE — minimal shared lock, only blocks FOR UPDATE
// Useful for FK-referencing queries that should not block non-key updates
var row = await ctx.Products
    .Where(p => p.Id == id)
    .ForKeyShare()
    .FirstOrDefaultAsync();
```

### Include with locking (PostgreSQL)

PostgreSQL automatically scopes the lock to the root table when a collection `Include` is present (emits `FOR UPDATE OF "t"`), so you can use `Include` directly without `AsSplitQuery()`:

```csharp
// Works — FOR UPDATE OF "p" is emitted automatically
var product = await ctx.Products
    .Include(p => p.OrderLines)
    .Where(p => p.Id == id)
    .ForUpdate()
    .FirstOrDefaultAsync();
```

### Queue processing pattern

A common use of `ForUpdate(LockBehavior.SkipLocked)` is a worker queue where multiple consumers race to claim items:

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

## Distributed locks

Distributed (advisory) locks let you coordinate across processes without tying the lock to a database row or transaction. They are session-scoped — the lock is held until you dispose the handle, or until the connection drops.

No transaction is required.

```csharp
// Acquire — blocks until available (optional timeout)
await using var handle = await ctx.Database.AcquireDistributedLockAsync("invoice:generate");
// ... critical section ...
// lock released automatically on dispose

// With a timeout — throws LockTimeoutException if not acquired within 5 s
await using var handle = await ctx.Database.AcquireDistributedLockAsync(
    "report:daily", TimeSpan.FromSeconds(5));

// With cancellation token
await using var handle = await ctx.Database.AcquireDistributedLockAsync(
    "report:daily", timeout: null, cancellationToken: ct);

// TryAcquire — returns null immediately if already held
var handle = await ctx.Database.TryAcquireDistributedLockAsync("invoice:generate");
if (handle is null)
    return Results.Conflict("Another process is generating the invoice.");
await using (handle) { /* critical section */ }

// Synchronous variants are also available
using var handle = ctx.Database.AcquireDistributedLock("report:daily");
var handle = ctx.Database.TryAcquireDistributedLock("report:daily");

// Check support at runtime
if (ctx.Database.SupportsDistributedLocks()) { ... }
```

### Lock keys

Keys are plain strings, up to **255 characters**. The library handles provider-specific encoding internally:

- **PostgreSQL** — hashed to a `bigint` via XxHash32 with a namespace prefix (`"EFLK"`); the hash is computed in-process so no extra round-trip is needed.
- **MySQL** — passed as-is for keys ≤ 64 UTF-8 bytes; longer keys are SHA-256 hashed to `lock:<hex58>` (64 chars). The `lock:` prefix is reserved.
- **SQL Server** — passed as-is (max 255 chars, enforced upstream).
- **Oracle** — passed as-is for keys ≤ 64 UTF-8 bytes; longer keys are SHA-256 hashed to `lock:<hex58>` (64 chars) to stay under `DBMS_LOCK.ALLOCATE_UNIQUE`'s 128-byte name limit. The `lock:` prefix is reserved.

### Provider-specific behavior

| Feature | PostgreSQL | MySQL | SQL Server |
|---------|-----------|-------|-----------|
| Native primitive | `pg_advisory_lock` | `GET_LOCK` | `sp_getapplock @LockOwner='Session'` |
| Timeout | `SET LOCAL lock_timeout` (ms) | `GET_LOCK(@key, seconds)` — rounded up to 1 s | `@LockTimeout` ms |
| Cancellation | Driver-level (best-effort) | `KILL QUERY` side-channel | Attention signal |

**Oracle** uses `DBMS_LOCK.ALLOCATE_UNIQUE` + `DBMS_LOCK.REQUEST` with `release_on_commit => FALSE` and timeout in integer seconds (rounded up to 1 s). Cancellation is best-effort via driver cancel. Requires `GRANT EXECUTE ON DBMS_LOCK` to the application user.

**MySQL timeout precision:** `GET_LOCK` timeout is in whole seconds. Sub-second timeouts are rounded up to 1 second.

**Cancellation caveat:** advisory lock SQL is a blocking database call. Cancellation sends a cancel signal to the driver; if the driver does not honor it before the timeout fires, the call completes via timeout. Always combine a `timeout` with the `CancellationToken` for bounded waits.

### Exception handling

```csharp
try
{
    await using var handle = await ctx.Database.AcquireDistributedLockAsync(
        "report:daily", TimeSpan.FromSeconds(5));
}
catch (LockTimeoutException)
{
    // Not acquired within the timeout
}
catch (LockAlreadyHeldException ex)
{
    // Same DbContext + connection attempted to acquire the same key twice
    // ex.Key contains the key name
}
catch (LockingConfigurationException)
{
    // Provider does not support distributed locks, or UseLocking() was not called
}
```

`LockAlreadyHeldException` is thrown synchronously before any database call when the same `(DbContext, connection, key)` triple is already registered. Acquiring the same key from two **different** `DbContext` instances on different connections will block (or return `null` for `TryAcquire`) as expected.

## Lock modes and behaviors

| Method | Generated SQL |
|--------|--------------|
| `ForUpdate()` | `FOR UPDATE` / `WITH (UPDLOCK, HOLDLOCK, ROWLOCK)` |
| `ForUpdate(LockBehavior.NoWait)` | `FOR UPDATE NOWAIT` / `SET LOCK_TIMEOUT 0` |
| `ForUpdate(LockBehavior.SkipLocked)` | `FOR UPDATE SKIP LOCKED` (PG/MySQL) / `WITH (UPDLOCK, ROWLOCK, READPAST)` (SQL Server) |
| `ForUpdate(LockBehavior.Wait, timeout)` | `SET LOCAL lock_timeout = '500ms'` (PG) / `SET SESSION innodb_lock_wait_timeout` (MySQL) / `SET LOCK_TIMEOUT 500` (SQL Server) |
| `ForShare()` | `FOR SHARE` (PostgreSQL/MySQL only) |
| `ForNoKeyUpdate()` | `FOR NO KEY UPDATE` (PostgreSQL only) |
| `ForKeyShare()` | `FOR KEY SHARE` (PostgreSQL only) |

## Exception handling

Lock failures throw typed exceptions from `EntityFrameworkCore.Locking.Exceptions`:

```csharp
try
{
    var row = await ctx.Products
        .Where(p => p.Id == id)
        .ForUpdate(LockBehavior.NoWait)
        .FirstOrDefaultAsync();
}
catch (LockTimeoutException ex)
{
    // Lock could not be acquired (NOWAIT or timeout exceeded)
}
catch (DeadlockException ex)
{
    // Deadlock detected — retry the transaction
}
catch (LockingConfigurationException ex)
{
    // Programmer error: missing transaction, unsupported query shape,
    // or unsupported lock mode for this provider
}
```

**Exception hierarchy:**
- `LockingException` (base)
  - `LockAcquisitionFailedException`
    - `LockTimeoutException` — timeout or NOWAIT failure
    - `DeadlockException` — deadlock victim
    - `LockAlreadyHeldException` — same key acquired twice on the same connection (distributed locks)
  - `LockingConfigurationException` — programmer error (missing transaction, unsupported query shape, provider not configured)

## Provider limitations

| Feature | PostgreSQL | MySQL | SQL Server | Oracle |
|---------|-----------|-------|-----------|--------|
| `ForUpdate` | ✓ | ✓ | ✓ | ✓ |
| `ForShare` | ✓ | ✓ | ✗ | ✗ |
| `ForNoKeyUpdate` | ✓ | ✗ | ✗ | ✗ |
| `ForKeyShare` | ✓ | ✗ | ✗ | ✗ |
| `SkipLocked` | ✓ | ✓ | ✓ (via `READPAST`) | ✓ |
| `NoWait` | ✓ | ✓ | ✓ | ✓ |
| Wait with timeout | ✓ (ms) | ✓ (ceil to 1s) | ✓ (ms) | ✓ (ceil to 1s) |

`ForNoKeyUpdate` and `ForKeyShare` are PostgreSQL-only extension methods available when the `EntityFrameworkCore.Locking.PostgreSQL` package is installed. Using `ForShare` on SQL Server or Oracle throws `LockingConfigurationException` — Oracle has no row-level shared lock (only table-level `LOCK TABLE ... IN SHARE MODE`).

**SQL Server `SkipLocked` limitation:** SQL Server uses `WITH (UPDLOCK, ROWLOCK, READPAST)` instead of `SKIP LOCKED`. `READPAST` only skips rows held under row-level or page-level locks — rows under a table-level lock are blocked rather than skipped. For typical queue-processing workloads this behaves identically to `SKIP LOCKED` on PostgreSQL/MySQL.

**MySQL timeout precision:** MySQL's `innodb_lock_wait_timeout` is in whole seconds. Sub-second timeouts are rounded up to 1 second.

**Oracle timeout precision:** Oracle's `FOR UPDATE WAIT n` and `DBMS_LOCK.REQUEST` both take integer seconds. Sub-second timeouts are rounded up to 1 second.

**Oracle advisory lock prerequisite:** `AcquireDistributedLockAsync` uses `DBMS_LOCK`, which requires an explicit grant. Run once as a DBA:
```sql
GRANT EXECUTE ON DBMS_LOCK TO <app_user>;
```
Without this grant, calls throw `LockingConfigurationException` (surfaced as ORA-06550 / PLS-00201).

## Unsupported query shapes

`UNION`, `EXCEPT`, `INTERSECT` with locking throw `LockingConfigurationException` at query execution time. Use per-query locks on individual queries before combining results.

`AsSplitQuery()` combined with locking throws `LockingConfigurationException` — use regular `Include()` instead (on PostgreSQL, `FOR UPDATE OF` is emitted automatically to handle outer joins).

## Supported database versions

| Database | Minimum version | Notes |
|----------|----------------|-------|
| PostgreSQL | **14** | Default minimum for Npgsql 8.x. PG 12+ works if you call `.SetPostgresVersion(12, 0)` in `UseNpgsql`. All locking features (`FOR NO KEY UPDATE`, `FOR KEY SHARE`, `SKIP LOCKED`, `NOWAIT`) have been available since PG 9.3/9.5. |
| MySQL | **8.0** | `FOR SHARE`, `SKIP LOCKED`, and `NOWAIT` were introduced in MySQL 8.0.1. MySQL 5.7 is not supported. |
| MariaDB | **10.6** | `SKIP LOCKED` requires 10.6+. `NOWAIT` requires 10.3+. `ForShare` emits `LOCK IN SHARE MODE` (MariaDB does not support the `FOR SHARE` syntax). |
| SQL Server | **2019** | All hints (`UPDLOCK`, `HOLDLOCK`, `ROWLOCK`, `READPAST`) and `SET LOCK_TIMEOUT` are available on all supported versions. Azure SQL Database is also supported. |
| Oracle | **19c** | `SELECT ... FOR UPDATE [NOWAIT \| WAIT n \| SKIP LOCKED]` and `DBMS_LOCK` have been stable for many releases. Oracle Database Free (23c) is the recommended dev/test image. |

## Target frameworks

`net8.0`, `net9.0`, `net10.0`

## Benchmarks

The `benchmarks/` directory contains BenchmarkDotNet benchmarks measuring the overhead added by the locking SQL generator and interceptor across all three providers.

```bash
dotnet run -c Release --project benchmarks/EntityFrameworkCore.Locking.Benchmarks -- --version=<x.y.z>
```

The `--version` argument is required and labels the results folder (`benchmarks/EntityFrameworkCore.Locking.Benchmarks/results/v<x.y.z>/`). Additional BenchmarkDotNet arguments (e.g. `--filter '*SqlGeneration*'`) can be appended after.

## License

[MIT](LICENSE)
