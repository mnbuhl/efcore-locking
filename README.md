# EntityFrameworkCore.Locking

Row-level pessimistic locking for EF Core via `ForUpdate()` and `ForShare()` LINQ extension methods. Supports PostgreSQL, MySQL, and SQL Server.

## Installation

```
dotnet add package EntityFrameworkCore.Locking.PostgreSQL  # Npgsql
dotnet add package EntityFrameworkCore.Locking.MySql       # Pomelo
dotnet add package EntityFrameworkCore.Locking.SqlServer   # Microsoft.Data.SqlClient
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
  - `LockingConfigurationException` — programmer error (missing transaction, unsupported query shape)

## Provider limitations

| Feature | PostgreSQL | MySQL | SQL Server |
|---------|-----------|-------|-----------|
| `ForUpdate` | ✓ | ✓ | ✓ |
| `ForShare` | ✓ | ✓ | ✗ |
| `ForNoKeyUpdate` | ✓ | ✗ | ✗ |
| `ForKeyShare` | ✓ | ✗ | ✗ |
| `SkipLocked` | ✓ | ✓ | ✓ (via `READPAST`) |
| `NoWait` | ✓ | ✓ | ✓ |
| Wait with timeout | ✓ (ms) | ✓ (ceil to 1s) | ✓ (ms) |

`ForNoKeyUpdate` and `ForKeyShare` are PostgreSQL-only extension methods available when the `EntityFrameworkCore.Locking.PostgreSQL` package is installed. Using `ForShare` on SQL Server throws `LockingConfigurationException`.

**SQL Server `SkipLocked` limitation:** SQL Server uses `WITH (UPDLOCK, ROWLOCK, READPAST)` instead of `SKIP LOCKED`. `READPAST` only skips rows held under row-level or page-level locks — rows under a table-level lock are blocked rather than skipped. For typical queue-processing workloads this behaves identically to `SKIP LOCKED` on PostgreSQL/MySQL.

**MySQL timeout precision:** MySQL's `innodb_lock_wait_timeout` is in whole seconds. Sub-second timeouts are rounded up to 1 second.

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

## Target frameworks

`net8.0`, `net9.0`, `net10.0`

## License

[MIT](LICENSE)
