using EntityFrameworkCore.Locking.Benchmarks.Entities;
using EntityFrameworkCore.Locking.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Locking.Benchmarks.Contexts;

public sealed class SqlServerBenchmarkDbContext : DbContext
{
    public DbSet<BenchmarkEntity> Items => Set<BenchmarkEntity>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlServer("Server=localhost;Database=bench").UseLocking();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<BenchmarkEntity>().ToTable("benchmark_entities");
}
