using EntityFrameworkCore.Locking;
using EntityFrameworkCore.Locking.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using QueueProcessor;

var connectionString =
    args.FirstOrDefault()
    ?? Environment.GetEnvironmentVariable("CONNECTION_STRING")
    ?? "Host=localhost;Database=queue_sample;Username=postgres;Password=postgres";

// --- Setup ---
var optionsBuilder = new DbContextOptionsBuilder<JobDbContext>()
    .UseNpgsql(connectionString)
    .UseLocking();

await using (var db = new JobDbContext(optionsBuilder.Options))
{
    await db.Database.EnsureCreatedAsync();

    // Seed 20 jobs if the table is empty
    if (!await db.Jobs.AnyAsync())
    {
        db.Jobs.AddRange(
            Enumerable
                .Range(1, 20)
                .Select(i => new Job
                {
                    Payload = $"task-{i:D3}",
                    CreatedAt = DateTime.UtcNow.AddSeconds(-i),
                })
        );
        await db.SaveChangesAsync();
        Console.WriteLine("Seeded 20 jobs.");
    }
}

// --- Run 4 concurrent workers ---
Console.WriteLine("Starting 4 concurrent workers...\n");

var workers = Enumerable
    .Range(1, 4)
    .Select(i => RunWorkerAsync($"worker-{i}", optionsBuilder.Options));
await Task.WhenAll(workers);

Console.WriteLine("\nAll workers finished.");

// --- Worker logic ---
static async Task RunWorkerAsync(string workerId, DbContextOptions<JobDbContext> options)
{
    while (true)
    {
        await using var db = new JobDbContext(options);
        await using var tx = await db.Database.BeginTransactionAsync();

        // ForUpdate(SkipLocked): grab the oldest pending job not already claimed by another worker.
        // Without SkipLocked, concurrent workers would block on the same row; this lets each worker
        // move on to the next available row immediately.
        var job = await db
            .Jobs.Where(j => j.Status == JobStatus.Pending)
            .OrderBy(j => j.CreatedAt)
            .ForUpdate(LockBehavior.SkipLocked)
            .FirstOrDefaultAsync();

        if (job is null)
        {
            await tx.RollbackAsync();
            break;
        }

        job.Status = JobStatus.Processing;
        job.WorkerId = workerId;
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        Console.WriteLine($"[{workerId}] claimed  job {job.Id:D3} ({job.Payload})");

        // Simulate work
        await Task.Delay(Random.Shared.Next(20, 80));

        await using var db2 = new JobDbContext(options);
        await using var tx2 = await db2.Database.BeginTransactionAsync();

        var processing = await db2.Jobs.Where(j => j.Id == job.Id).ForUpdate().FirstAsync();

        processing.Status = JobStatus.Done;
        processing.ProcessedAt = DateTime.UtcNow;
        await db2.SaveChangesAsync();
        await tx2.CommitAsync();

        Console.WriteLine($"[{workerId}] finished job {job.Id:D3} ({job.Payload})");
    }
}
