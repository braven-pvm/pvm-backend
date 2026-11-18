namespace Pvm.Infrastructure.Persistence.Entities;

/// <summary>A staff name the phone app offers before a batch (design 6.10). No password, no email address.</summary>
public sealed class ScannerStaffEntity
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public bool Active { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
