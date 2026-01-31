namespace InventoryBot.Domain.Enums;

public enum UserRole
{
    User = 0,
    Admin = 1,
    Deputy = 2, // O'rinbosar
    Storekeeper = 3 // Omborchi
}

public enum UserStatus
{
    Pending = 0,
    Active = 1,
    Blocked = 2
}
