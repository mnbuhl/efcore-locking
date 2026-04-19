# Samples

These samples target **.NET 10** and reference the library source directly. Each requires a running PostgreSQL instance.

## Quick start

```bash
# Start PostgreSQL via Docker Compose (creates both databases automatically)
docker compose -f samples/docker-compose.yml up -d

# Run a sample (connection string optional — defaults to localhost)
dotnet run --project samples/QueueProcessor
dotnet run --project samples/InventoryApi
```

---

## QueueProcessor

**Use case:** Distributed worker queue — multiple concurrent consumers race to claim jobs without stepping on each other. A post-run maintenance sweep uses an advisory lock to ensure only one process requeues stale jobs.

**Key APIs:** `ForUpdate(LockBehavior.SkipLocked)`, `TryAcquireDistributedLockAsync`

```
samples/QueueProcessor/
├── Program.cs          # Seeds 20 jobs, runs 4 concurrent workers
├── JobDbContext.cs
├── Models.cs
└── QueueProcessor.csproj
```

**How it works:**

Each worker opens a transaction and issues:

```csharp
var job = await db.Jobs
    .Where(j => j.Status == JobStatus.Pending)
    .OrderBy(j => j.CreatedAt)
    .ForUpdate(LockBehavior.SkipLocked)
    .FirstOrDefaultAsync();
```

`SkipLocked` means a worker immediately skips any row another worker has already locked — no blocking, no duplicate processing. When no unlocked pending rows remain, the worker exits.

After all workers finish, a maintenance sweep requeues any jobs stuck in `Processing` for more than 5 minutes:

```csharp
await using var sweepHandle = await db.TryAcquireDistributedLockAsync("jobs:maintenance-sweep");
if (sweepHandle is null)
{
    Console.WriteLine("Another process is already running the sweep — skipping.");
}
else
{
    // requeue stale jobs ...
}
```

`TryAcquireDistributedLockAsync` returns `null` immediately if another process holds the lock, so the sweep never runs twice in parallel.

**Override connection string:**

```bash
dotnet run --project samples/QueueProcessor "Host=myhost;Database=queue;Username=...;Password=..."
# or
CONNECTION_STRING="..." dotnet run --project samples/QueueProcessor
```

---

## InventoryApi

**Use case:** HTTP API for inventory management — demonstrates row-level locking and advisory lock patterns on a single `Products` table.

**Key APIs:** `ForUpdate(NoWait)`, `ForShare()`, `ForNoKeyUpdate()`, `ForUpdate(Wait, timeout)`, `TryAcquireDistributedLockAsync`, `AcquireDistributedLockAsync`

```
samples/InventoryApi/
├── Program.cs          # All endpoints in one file (minimal API)
├── InventoryDbContext.cs
├── Models.cs
└── InventoryApi.csproj
```

**Endpoints:**

| Method | Path | Lock | Pattern |
|--------|------|------|---------|
| `GET` | `/products` | none | Plain read |
| `GET` | `/products/{id}/price` | `ForShare` | Consistent read, allows concurrent readers |
| `POST` | `/products/{id}/reserve?qty=N` | `ForUpdate(NoWait)` | Exclusive, fail-fast → 409 on contention |
| `PUT` | `/products/{id}/price` | `ForNoKeyUpdate` | Exclusive on non-key columns, allows FK readers |
| `POST` | `/products/{id}/reserve-with-timeout?qty=N` | `ForUpdate(Wait, 500ms)` | Exclusive, wait up to 500 ms → 503 on timeout |
| `POST` | `/inventory/snapshot` | `TryAcquireDistributedLock` | Advisory lock, non-blocking → 409 if already running |
| `POST` | `/products/price-sync` | `AcquireDistributedLock(3s)` | Advisory lock, wait up to 3 s → 409 on timeout |

**Example calls:**

```bash
# Check price (shared lock)
curl http://localhost:5000/products/1/price

# Reserve 5 units (exclusive, fail-fast)
curl -X POST "http://localhost:5000/products/1/reserve?qty=5"

# Reserve with timeout
curl -X POST "http://localhost:5000/products/1/reserve-with-timeout?qty=5"

# Update price (ForNoKeyUpdate)
curl -X PUT "http://localhost:5000/products/1/price?newPrice=14.99"

# Take inventory snapshot (advisory lock, non-blocking — 409 if already running)
curl -X POST http://localhost:5000/inventory/snapshot

# Sync prices (advisory lock, waits up to 3 s — 409 on timeout)
curl -X POST http://localhost:5000/products/price-sync
```

**API explorer:** Once running, open `http://localhost:5000/scalar` for the interactive Scalar UI (built on the .NET 10 OpenAPI endpoint at `/openapi/v1.json`).

**Override connection string:**

```bash
dotnet run --project samples/InventoryApi \
  --ConnectionStrings:Inventory "Host=myhost;Database=inventory;Username=...;Password=..."
```
