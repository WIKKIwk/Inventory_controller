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

public interface IAppConfigRepository
{
    Task<string?> GetValueAsync(string key);
    Task SetValueAsync(string key, string value);
}
