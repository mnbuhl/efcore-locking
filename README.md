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

## Lock modes and behaviors

| Method | Generated SQL |
|--------|--------------|
| `ForUpdate()` | `FOR UPDATE` / `WITH (UPDLOCK, HOLDLOCK, ROWLOCK)` |
| `ForUpdate(LockBehavior.NoWait)` | `FOR UPDATE NOWAIT` / `SET LOCK_TIMEOUT 0` |
| `ForUpdate(LockBehavior.SkipLocked)` | `FOR UPDATE SKIP LOCKED` (PG/MySQL) / `WITH (UPDLOCK, ROWLOCK, READPAST)` (SQL Server) |
| `ForUpdate(LockBehavior.Wait, timeout)` | `SET LOCAL lock_timeout = '500ms'` (PG) / `SET SESSION innodb_lock_wait_timeout` (MySQL) / `SET LOCK_TIMEOUT 500` (SQL Server) |
| `ForShare()` | `FOR SHARE` (PostgreSQL/MySQL only) |

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

SQL Server does not support `FOR SHARE`. Using it throws `LockingConfigurationException`.

`ForNoKeyUpdate` and `ForKeyShare` are PostgreSQL-only extension methods available when the `EntityFrameworkCore.Locking.PostgreSQL` package is installed.

**SQL Server `SkipLocked` limitation:** SQL Server uses `WITH (UPDLOCK, ROWLOCK, READPAST)` instead of `SKIP LOCKED`. `READPAST` only skips rows held under row-level or page-level locks. Rows held under a table-level lock are blocked rather than skipped. For typical queue-processing workloads (row locks) this behaves identically to `SKIP LOCKED` on PostgreSQL/MySQL.

## Unsupported query shapes

`UNION`, `EXCEPT`, `INTERSECT` with locking throw `LockingConfigurationException` at query execution time. Use per-query locks on individual queries before combining results.

## Target frameworks

`net8.0`, `net9.0`, `net10.0`

## License

MIT
