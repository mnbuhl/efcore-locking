using Microsoft.EntityFrameworkCore;

namespace InventoryApi;

public class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
}
