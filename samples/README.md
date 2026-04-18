# Samples

These samples target **.NET 10** and reference the library source directly. Each requires a running PostgreSQL instance.

## Quick start

```bash
# Start PostgreSQL via Docker
docker run -d --name pg-samples \
  -e POSTGRES_PASSWORD=postgres \
  -p 5432:5432 postgres:17

# Run a sample (connection string optional — defaults to localhost)
dotnet run --project samples/QueueProcessor
dotnet run --project samples/InventoryApi
```

---

## QueueProcessor

**Use case:** Distributed worker queue — multiple concurrent consumers race to claim jobs without stepping on each other.

**Key API:** `ForUpdate(LockBehavior.SkipLocked)`

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

**Override connection string:**

```bash
dotnet run --project samples/QueueProcessor "Host=myhost;Database=queue;Username=...;Password=..."
# or
CONNECTION_STRING="..." dotnet run --project samples/QueueProcessor
```

---

## InventoryApi

**Use case:** HTTP API for inventory management — demonstrates four locking patterns on a single `Products` table.

**Key APIs:** `ForUpdate(NoWait)`, `ForShare()`, `ForNoKeyUpdate()`, `ForUpdate(Wait, timeout)`

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
```

**Override connection string:**

```bash
dotnet run --project samples/InventoryApi \
  --ConnectionStrings:Inventory "Host=myhost;Database=inventory;Username=...;Password=..."
```
