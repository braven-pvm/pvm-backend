using Pvm.Application.Shoprite;

namespace Pvm.Infrastructure.Persistence.Entities;

/// <summary>
/// A Shoprite store or distribution centre. <see cref="BranchCode"/> is the Shoprite branch
/// number and also the Acumatica customer ID, so an invoice finds its GLN by its customer.
/// </summary>
public sealed class ShopriteDeliveryLocationEntity
{
    public Guid Id { get; set; }
    public required string BranchCode { get; set; }
    public required string Gln { get; set; }
    public required string Name { get; set; }
    public ShopriteLocationType LocationType { get; set; }
    public bool IsVerified { get; set; }
    public required string UpdatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
