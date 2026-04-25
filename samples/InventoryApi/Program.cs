using EntityFrameworkCore.Locking;
using EntityFrameworkCore.Locking.Exceptions;
using EntityFrameworkCore.Locking.PostgreSQL;
using InventoryApi;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var connectionString =
    builder.Configuration.GetConnectionString("Inventory")
    ?? "Host=localhost;Database=inventory_sample;Username=postgres;Password=postgres";

builder.Services.AddDbContext<InventoryDbContext>(o => o.UseNpgsql(connectionString).UseLocking());

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();

// Ensure schema and seed data on startup
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    await db.Database.EnsureCreatedAsync();
    if (!await db.Products.AnyAsync())
    {
        db.Products.AddRange(
            new Product
            {
                Name = "Widget",
                Price = 9.99m,
                Stock = 100,
            },
            new Product
            {
                Name = "Gadget",
                Price = 24.99m,
                Stock = 50,
            },
            new Product
            {
                Name = "Gizmo",
                Price = 4.99m,
                Stock = 200,
            }
        );
        await db.SaveChangesAsync();
    }
}

// GET /products — list all (no lock needed)
app.MapGet("/products", async (InventoryDbContext db) => await db.Products.ToListAsync());

// GET /products/{id}/price — ForShare: allows concurrent readers, blocks writers
// Use this when you need a consistent read without preventing other price readers.
app.MapGet(
    "/products/{id:int}/price",
    async (int id, InventoryDbContext db) =>
    {
        await using var tx = await db.Database.BeginTransactionAsync();

        var product = await db.Products.Where(p => p.Id == id).ForShare().FirstOrDefaultAsync();

        await tx.CommitAsync();

        return product is null
            ? Results.NotFound()
            : Results.Ok(
                new
                {
                    product.Id,
                    product.Name,
                    product.Price,
                }
            );
    }
);

// POST /products/{id}/reserve?qty=N — ForUpdate(NoWait): exclusive lock, fail fast if contended.
// Returns 409 Conflict if another transaction holds the row; the client should retry.
app.MapPost(
    "/products/{id:int}/reserve",
    async (int id, int qty, InventoryDbContext db) =>
    {
        await using var tx = await db.Database.BeginTransactionAsync();

        Product? product;
        try
        {
            product = await db.Products.Where(p => p.Id == id).ForUpdate(LockBehavior.NoWait).FirstOrDefaultAsync();
        }
        catch (LockTimeoutException)
        {
            return Results.Conflict(new { error = "Product is currently being updated. Please retry." });
        }

        if (product is null)
            return Results.NotFound();

        if (product.Stock < qty)
            return Results.UnprocessableEntity(new { error = "Insufficient stock.", available = product.Stock });

        product.Stock -= qty;
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return Results.Ok(
            new
            {
                product.Id,
                product.Name,
                reserved = qty,
                remaining = product.Stock,
            }
        );
    }
);

// PUT /products/{id}/price — ForNoKeyUpdate: blocks writers but allows FK-referencing readers.
// Safer than ForUpdate when updating non-key columns because it doesn't block FOR KEY SHARE queries.
app.MapPut(
    "/products/{id:int}/price",
    async (int id, decimal newPrice, InventoryDbContext db) =>
    {
        if (newPrice <= 0)
            return Results.BadRequest(new { error = "Price must be positive." });

        await using var tx = await db.Database.BeginTransactionAsync();

        var product = await db.Products.Where(p => p.Id == id).ForNoKeyUpdate().FirstOrDefaultAsync();

        if (product is null)
            return Results.NotFound();

        product.Price = newPrice;
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return Results.Ok(
            new
            {
                product.Id,
                product.Name,
                product.Price,
            }
        );
    }
);

// POST /products/{id}/reserve-with-timeout?qty=N — ForUpdate(Wait, timeout): wait up to 500ms.
// Shows the timed-wait pattern; LockTimeoutException is thrown if the lock isn't acquired in time.
app.MapPost(
    "/products/{id:int}/reserve-with-timeout",
    async (int id, int qty, InventoryDbContext db) =>
    {
        await using var tx = await db.Database.BeginTransactionAsync();

        Product? product;
        try
        {
            product = await db
                .Products.Where(p => p.Id == id)
                .ForUpdate(LockBehavior.Wait, TimeSpan.FromMilliseconds(500))
                .FirstOrDefaultAsync();
        }
        catch (LockTimeoutException)
        {
            return Results.StatusCode(503); // Service Unavailable — lock not acquired within timeout
        }
        catch (DeadlockException)
        {
            return Results.StatusCode(503); // Deadlock — client should retry
        }

        if (product is null)
            return Results.NotFound();

        if (product.Stock < qty)
            return Results.UnprocessableEntity(new { error = "Insufficient stock.", available = product.Stock });

        product.Stock -= qty;
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return Results.Ok(
            new
            {
                product.Id,
                product.Name,
                reserved = qty,
                remaining = product.Stock,
            }
        );
    }
);

// POST /inventory/snapshot — TryAcquireDistributedLockAsync: non-blocking.
// If another process is already taking a snapshot, returns 409 immediately rather than waiting.
app.MapPost(
    "/inventory/snapshot",
    async (InventoryDbContext db) =>
    {
        await using var handle = await db.Database.TryAcquireDistributedLockAsync("inventory:snapshot");
        if (handle is null)
            return Results.Conflict(new { error = "A snapshot is already in progress." });

        var products = await db.Products.ToListAsync();

        // simulate delay
        await Task.Delay(1000);

        return Results.Ok(
            products.Select(p => new
            {
                p.Id,
                p.Name,
                p.Stock,
                p.Price,
                TakenAt = DateTime.UtcNow,
            })
        );
    }
);

// POST /products/price-sync — AcquireDistributedLockAsync with timeout.
// Waits up to 3 seconds for the lock; returns 409 if another sync is still running by then.
// Use this when the operation is short-lived and callers can tolerate a brief wait.
app.MapPost(
    "/products/price-sync",
    async (InventoryDbContext db) =>
    {
        try
        {
            await using var handle = await db.Database.AcquireDistributedLockAsync(
                "products:price-sync",
                timeout: TimeSpan.FromSeconds(3)
            );

            // Lock held — simulate applying a price adjustment from an external feed
            var products = await db.Products.ToListAsync();
            foreach (var product in products)
                product.Price = Math.Round(product.Price * 1.05m, 2);

            await db.SaveChangesAsync();
            return Results.Ok(new { synced = products.Count });
        }
        catch (LockTimeoutException)
        {
            return Results.Conflict(new { error = "Price sync is already running. Try again shortly." });
        }
    }
);

app.Run();
