namespace Pvm.Infrastructure.Persistence.Entities;

/// <summary>
/// A phone that scans documents (design 6.10). The device signs in, not the person. Only the
/// SHA-256 hash of its key is stored; revoking clears it.
/// </summary>
public sealed class ScannerDeviceEntity
{
    public Guid Id { get; set; }

    /// <summary>Such as "Dispatch phone 1"; unique among devices that are not revoked.</summary>
    public required string Name { get; set; }

    /// <summary>Lower-case SHA-256 hex of the device key; null until enrolled and after a revoke.</summary>
    public string? KeyHash { get; set; }

    public required string CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? EnrolledAt { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }

    public string? RevokedBy { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}
