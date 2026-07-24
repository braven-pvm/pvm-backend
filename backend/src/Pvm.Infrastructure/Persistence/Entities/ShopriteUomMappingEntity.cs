using Pvm.Domain.Invoices;

namespace Pvm.Infrastructure.Persistence.Entities;

public sealed class ShopriteUomMappingEntity
{
    public Guid Id { get; set; }
    public required string AcumaticaInventoryId { get; set; }
    public required string AcumaticaUom { get; set; }
    public ShopriteMeasurementUnit ShopriteUom { get; set; }
    public bool IsVerified { get; set; }
    public required string UpdatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
