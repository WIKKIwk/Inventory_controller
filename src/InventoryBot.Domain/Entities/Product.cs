using InventoryBot.Domain.Enums;

namespace InventoryBot.Domain.Entities;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public UnitType UnitType { get; set; }
    public int WarehouseId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation property
    // public Warehouse Warehouse { get; set; } // Omitted to avoid circular deps for now or complexity
}
