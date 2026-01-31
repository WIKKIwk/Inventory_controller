using InventoryBot.Application.Interfaces;
using InventoryBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InventoryBot.Infrastructure.Persistence.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly AppDbContext _context;

    public ProductRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(Product product)
    {
        await _context.Products.AddAsync(product);
        await _context.SaveChangesAsync();
    }

    public async Task<List<Product>> GetAllByWarehouseIdAsync(int warehouseId)
    {
        return await _context.Products
            .Where(p => p.WarehouseId == warehouseId)
            .ToListAsync();
    }
}
