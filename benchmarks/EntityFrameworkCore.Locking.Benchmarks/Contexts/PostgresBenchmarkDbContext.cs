using EntityFrameworkCore.Locking.Benchmarks.Entities;
using EntityFrameworkCore.Locking.PostgreSQL;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Locking.Benchmarks.Contexts;

public sealed class PostgresBenchmarkDbContext : DbContext
{
    public DbSet<BenchmarkEntity> Items => Set<BenchmarkEntity>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseNpgsql("Host=localhost;Database=bench").UseLocking();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<BenchmarkEntity>().ToTable("benchmark_entities");
}
