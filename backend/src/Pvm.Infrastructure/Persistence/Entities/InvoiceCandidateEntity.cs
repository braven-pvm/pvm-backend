namespace Pvm.Infrastructure.Persistence.Entities;

public sealed class InvoiceCandidateEntity
{
    public Guid Id { get; set; }
    public required string AcumaticaInvoiceId { get; set; }
    public required string InvoiceNumber { get; set; }
    public required string CustomerAccount { get; set; }
    public string? CustomerLocation { get; set; }
    public string? ShopritePurchaseOrderNumber { get; set; }
    public string? SupplierGln { get; set; }
    public string? StoreDcGln { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string Status { get; set; }
    public string? SourceJson { get; set; }
    public string? CanonicalJson { get; set; }
    public string? ValidationJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
