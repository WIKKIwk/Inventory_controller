using InventoryBot.Domain.Enums;

namespace InventoryBot.Domain.Entities;

public class BotUser
{
    public long ChatId { get; set; }
    public string? Username { get; set; }
    public string? FullName { get; set; }
    public UserRole Role { get; set; } = UserRole.User;
    public UserStatus Status { get; set; } = UserStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
