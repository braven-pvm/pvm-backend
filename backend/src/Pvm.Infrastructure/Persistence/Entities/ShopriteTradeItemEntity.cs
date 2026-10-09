using Pvm.Application.Shoprite;
using Pvm.Domain.Invoices;

namespace Pvm.Infrastructure.Persistence.Entities;

/// <summary>
/// The GTIN Shoprite orders one Acumatica item under, per delivery type. Stores order boxes and
/// distribution centres order cases, so one item and unit can have two rows.
/// </summary>
public sealed class ShopriteTradeItemEntity
{
    public Guid Id { get; set; }
    public required string AcumaticaInventoryId { get; set; }
    public required string AcumaticaUom { get; set; }
    public ShopriteLocationType DeliversTo { get; set; }
    public required string Gtin { get; set; }
    public int AcumaticaUnitsPerShopriteUnit { get; set; }
    public decimal ShopritePackSize { get; set; }
    public ShopriteMeasurementUnit ShopriteUom { get; set; }
    public bool IsVerified { get; set; }
    public required string UpdatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
