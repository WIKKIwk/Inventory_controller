using InventoryBot.Application.Interfaces;
using InventoryBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InventoryBot.Infrastructure.Persistence.Repositories;

public class CustomerRepository : ICustomerRepository
{
    private readonly AppDbContext _context;

    public CustomerRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<Customer>> GetAllAsync()
    {
        return await _context.Customers.ToListAsync();
    }

    public async Task<Customer?> GetByIdAsync(int id)
    {
        return await _context.Customers.FindAsync(id);
    }

    public async Task<Customer?> GetByNameAsync(string name)
    {
        return await _context.Customers.FirstOrDefaultAsync(c => c.Name == name);
    }

    public async Task AddAsync(Customer customer)
    {
        await _context.Customers.AddAsync(customer);
        await _context.SaveChangesAsync();
    }
}
