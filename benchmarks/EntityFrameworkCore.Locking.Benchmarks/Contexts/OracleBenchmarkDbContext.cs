using EntityFrameworkCore.Locking.Benchmarks.Entities;
using EntityFrameworkCore.Locking.Oracle;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Locking.Benchmarks.Contexts;

public sealed class OracleBenchmarkDbContext : DbContext
{
    public DbSet<BenchmarkEntity> Items => Set<BenchmarkEntity>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseOracle("User Id=bench;Password=bench;Data Source=//localhost:1521/FREEPDB1").UseLocking();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<BenchmarkEntity>().ToTable("benchmark_entities");
}
