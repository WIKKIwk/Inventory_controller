using System;

namespace InventoryBot.Domain.Enums;

public enum UserRole
{
    User = 0,
    Admin = 1,
    Deputy = 2, // O'rinbosar
    Storekeeper = 3 // Omborchi
}

[Flags]
public enum UserRoles
{
    None = 0,
    User = 1,
    Admin = 2,
    Deputy = 4,
    Storekeeper = 8
}

public enum UserStatus
{
    Pending = 0,
    Active = 1,
    Blocked = 2
}
