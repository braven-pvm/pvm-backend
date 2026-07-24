namespace Pvm.Infrastructure.Persistence.Entities;

public sealed class ShopriteItemMappingEntity
{
    public Guid Id { get; set; }
    public required string AcumaticaInventoryId { get; set; }
    public required string ShopriteBuyerItemId { get; set; }
    public required string Gtin { get; set; }
    public bool IsVerified { get; set; }
    public required string UpdatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
