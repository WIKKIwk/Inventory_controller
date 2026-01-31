using InventoryBot.Domain.Entities;
using InventoryBot.Domain.Enums;

namespace InventoryBot.Application.Interfaces;

public interface IUserRepository
{
    Task<BotUser?> GetByChatIdAsync(long chatId);
    Task AddAsync(BotUser user);
    Task UpdateAsync(BotUser user);
    Task<List<BotUser>> GetPendingUsersAsync(); // For notification
    Task<List<BotUser>> GetAllUsersAsync(); 
}

public interface IWarehouseRepository
{
    Task<List<Warehouse>> GetAllAsync();
    Task<Warehouse?> GetByIdAsync(int id);
    Task<Warehouse?> GetByNameAsync(string name);
    Task AddAsync(Warehouse warehouse);
}

public interface ICustomerRepository
{
    Task<List<Customer>> GetAllAsync();
    Task<Customer?> GetByIdAsync(int id);
    Task<Customer?> GetByNameAsync(string name);
    Task AddAsync(Customer customer);
}

public interface IProductRepository
{
    Task AddAsync(Product product);
    Task<List<Product>> GetAllByWarehouseIdAsync(int warehouseId);
}

public interface IAppConfigRepository
{
    Task<string?> GetValueAsync(string key);
    Task SetValueAsync(string key, string value);
}
