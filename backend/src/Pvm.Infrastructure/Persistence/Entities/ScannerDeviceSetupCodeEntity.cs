namespace Pvm.Infrastructure.Persistence.Entities;

/// <summary>A one-time setup code for a scanner device: valid 15 minutes, usable once (design 6.10).</summary>
public sealed class ScannerDeviceSetupCodeEntity
{
    public Guid Id { get; set; }

    public Guid DeviceId { get; set; }

    /// <summary>Lower-case SHA-256 hex of the code; the code itself is never stored.</summary>
    public required string CodeHash { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// When the code enrolled the device. A concurrency token: the enrol update only succeeds while
    /// it is still null, so one code cannot enrol twice under a race.
    /// </summary>
    public DateTimeOffset? UsedAt { get; set; }

    public required string CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
