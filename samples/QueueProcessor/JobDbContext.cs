using Microsoft.EntityFrameworkCore;

namespace QueueProcessor;

public class JobDbContext(DbContextOptions<JobDbContext> options) : DbContext(options)
{
    public DbSet<Job> Jobs => Set<Job>();
}
