using InventoryBot.Application.Interfaces;
using InventoryBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InventoryBot.Infrastructure.Persistence.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<BotUser?> GetByChatIdAsync(long chatId)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.ChatId == chatId);
    }

    public async Task AddAsync(BotUser user)
    {
        await _context.Users.AddAsync(user);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(BotUser user)
    {
        _context.Users.Update(user);
        await _context.SaveChangesAsync();
    }

    public async Task<List<BotUser>> GetPendingUsersAsync()
    {
        return await _context.Users.Where(u => u.Status == Domain.Enums.UserStatus.Pending).ToListAsync();
    }
    
    public async Task<List<BotUser>> GetAllUsersAsync()
    {
        return await _context.Users.ToListAsync();
    }
}

public class AppConfigRepository : IAppConfigRepository
{
    private readonly AppDbContext _context;

    public AppConfigRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<string?> GetValueAsync(string key)
    {
        var config = await _context.Configs.FirstOrDefaultAsync(c => c.Key == key);
        return config?.Value;
    }

    public async Task SetValueAsync(string key, string value)
    {
        var config = await _context.Configs.FirstOrDefaultAsync(c => c.Key == key);
        if (config == null)
        {
            config = new AppConfig { Key = key, Value = value };
            await _context.Configs.AddAsync(config);
        }
        else
        {
            config.Value = value;
            _context.Configs.Update(config);
        }
        await _context.SaveChangesAsync();
    }
}
