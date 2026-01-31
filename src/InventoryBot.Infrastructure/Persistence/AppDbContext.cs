using InventoryBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InventoryBot.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public DbSet<BotUser> Users { get; set; }
    public DbSet<AppConfig> Configs { get; set; }
    public DbSet<Warehouse> Warehouses { get; set; }
    public DbSet<Customer> Customers { get; set; }
    public DbSet<Product> Products { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<BotUser>().HasKey(u => u.ChatId);
        
        modelBuilder.Entity<AppConfig>().HasKey(c => c.Key);
    }
}
