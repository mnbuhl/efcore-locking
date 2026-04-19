using EntityFrameworkCore.Locking.Benchmarks.Entities;
using EntityFrameworkCore.Locking.MySql;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Locking.Benchmarks.Contexts;

public sealed class MySqlBenchmarkDbContext : DbContext
{
    public DbSet<BenchmarkEntity> Items => Set<BenchmarkEntity>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder
            .UseMySql("Server=localhost;Database=bench", new MySqlServerVersion(new Version(8, 0, 0)))
            .UseLocking();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<BenchmarkEntity>().ToTable("benchmark_entities");
}
