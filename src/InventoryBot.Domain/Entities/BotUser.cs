using InventoryBot.Domain.Enums;

namespace InventoryBot.Domain.Entities;

public class BotUser
{
    public long ChatId { get; set; }
    public string? Username { get; set; }
    public string? FullName { get; set; }
    public UserRole Role { get; set; } = UserRole.User;
    public UserRoles Roles { get; set; } = UserRoles.None;
    public UserStatus Status { get; set; } = UserStatus.Pending;
    public string? LanguageCode { get; set; }
    public int? WarehouseId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
