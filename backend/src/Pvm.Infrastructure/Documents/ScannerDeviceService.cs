using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Documents;

/// <summary>
/// Scanner device enrolment (design 6.10): one-time setup codes, the device key, revoke, and the
/// key lookup for the <c>DeviceKey</c> authentication scheme. Codes and keys are 32 random bytes in
/// base64url; only their SHA-256 hashes are stored.
/// </summary>
public sealed class ScannerDeviceService(PvmDbContext dbContext)
{
    public const int MaxNameLength = 100;

    public const string KeyPrefix = "pvmd_";

    public static readonly TimeSpan SetupCodeLifetime = TimeSpan.FromMinutes(15);

    /// <summary>LastSeenAt is written at most once in this interval per device.</summary>
    public static readonly TimeSpan LastSeenInterval = TimeSpan.FromMinutes(1);

    private const int MaxSecretLength = 256;

    /// <summary>Lower-case SHA-256 hex of the UTF-8 text.</summary>
    public static string Hash(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static ScannerDeviceState StateOf(ScannerDeviceEntity device)
        => device.RevokedAt is not null
            ? ScannerDeviceState.Revoked
            : device.EnrolledAt is not null ? ScannerDeviceState.Active : ScannerDeviceState.Pending;

    public async Task<IReadOnlyList<ScannerDeviceEntity>> ListAsync(CancellationToken cancellationToken)
        => await dbContext.ScannerDevices
            .AsNoTracking()
            .OrderBy(device => device.RevokedAt != null)
            .ThenBy(device => device.Name)
            .ToListAsync(cancellationToken);

    public async Task<CreateDeviceResult> CreateAsync(string name, string createdBy, CancellationToken cancellationToken)
    {
        var lowered = name.ToLowerInvariant();
        if (await dbContext.ScannerDevices.AnyAsync(
                device => device.RevokedAt == null && device.Name.ToLower() == lowered,
                cancellationToken))
        {
            return new CreateDeviceResult(CreateDeviceOutcome.NameTaken, null);
        }

        var now = DateTimeOffset.UtcNow;
        var device = new ScannerDeviceEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedBy = createdBy,
            CreatedAt = now
        };
        var (code, row) = NewCode(device.Id, createdBy, now);

        dbContext.ScannerDevices.Add(device);
        dbContext.ScannerDeviceSetupCodes.Add(row);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        })
        {
            // ux_scanner_devices_active_name_lower: a concurrent add of the same name won.
            dbContext.ChangeTracker.Clear();
            return new CreateDeviceResult(CreateDeviceOutcome.NameTaken, null);
        }

        return new CreateDeviceResult(CreateDeviceOutcome.Created, new IssuedSetupCode(device, code, row.ExpiresAt));
    }

    /// <summary>A new setup code for an existing device; earlier unused codes stop working.</summary>
    public async Task<NewSetupCodeResult> NewSetupCodeAsync(Guid deviceId, string createdBy, CancellationToken cancellationToken)
    {
        var device = await dbContext.ScannerDevices.SingleOrDefaultAsync(item => item.Id == deviceId, cancellationToken);
        if (device is null)
        {
            return new NewSetupCodeResult(NewSetupCodeOutcome.NotFound, null);
        }

        if (device.RevokedAt is not null)
        {
            return new NewSetupCodeResult(NewSetupCodeOutcome.Revoked, null);
        }

        var now = DateTimeOffset.UtcNow;
        var open = await dbContext.ScannerDeviceSetupCodes
            .Where(code => code.DeviceId == deviceId && code.UsedAt == null && code.ExpiresAt > now)
            .ToListAsync(cancellationToken);
        foreach (var earlier in open)
        {
            earlier.ExpiresAt = now;
        }

        var (plain, row) = NewCode(deviceId, createdBy, now);
        dbContext.ScannerDeviceSetupCodes.Add(row);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new NewSetupCodeResult(NewSetupCodeOutcome.Created, new IssuedSetupCode(device, plain, row.ExpiresAt));
    }

    public async Task<RevokeOutcome> RevokeAsync(Guid deviceId, string revokedBy, CancellationToken cancellationToken)
    {
        var device = await dbContext.ScannerDevices.SingleOrDefaultAsync(item => item.Id == deviceId, cancellationToken);
        if (device is null)
        {
            return RevokeOutcome.NotFound;
        }

        if (device.RevokedAt is null)
        {
            device.RevokedAt = DateTimeOffset.UtcNow;
            device.RevokedBy = revokedBy;
        }

        device.KeyHash = null;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent revoke won; it cleared the key as well.
            dbContext.ChangeTracker.Clear();
        }

        return RevokeOutcome.Revoked;
    }

    public Task<ScannerDeviceEntity?> FindAsync(Guid deviceId, CancellationToken cancellationToken)
        => dbContext.ScannerDevices.AsNoTracking().SingleOrDefaultAsync(item => item.Id == deviceId, cancellationToken);

    /// <summary>
    /// Enrols the device of a valid setup code and returns its new key, or null for any invalid
    /// code: unknown, used, expired, or a revoked device. The caller must not say which.
    /// </summary>
    public async Task<DeviceEnrolment?> EnrolAsync(string? code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > MaxSecretLength)
        {
            return null;
        }

        var hash = Hash(code.Trim());
        var now = DateTimeOffset.UtcNow;
        var setupCode = await dbContext.ScannerDeviceSetupCodes.SingleOrDefaultAsync(item => item.CodeHash == hash, cancellationToken);
        if (setupCode is null || setupCode.UsedAt is not null || setupCode.ExpiresAt <= now)
        {
            return null;
        }

        var device = await dbContext.ScannerDevices.SingleOrDefaultAsync(item => item.Id == setupCode.DeviceId, cancellationToken);
        if (device is null || device.RevokedAt is not null)
        {
            return null;
        }

        var key = KeyPrefix + RandomSecret();
        setupCode.UsedAt = now;
        device.KeyHash = Hash(key);
        device.EnrolledAt = now;
        try
        {
            // Conditional updates in one transaction (concurrency tokens): the code's UPDATE
            // carries "UsedAt" IS NULL and the "ExpiresAt" that was read (later than now), and the
            // device's UPDATE carries "RevokedAt" IS NULL. A second request with the same code, a
            // code replaced meanwhile, or a device revoked meanwhile updates no row; everything
            // rolls back, so no key is written and the code stays unused.
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return null;
        }

        return new DeviceEnrolment(device.Id, device.Name, key);
    }

    /// <summary>
    /// The device of a key, or null when the key is unknown or the device is revoked. Writes
    /// LastSeenAt at most once a minute.
    /// </summary>
    public async Task<ScannerDeviceEntity?> AuthenticateAsync(string key, CancellationToken cancellationToken)
    {
        if (!key.StartsWith(KeyPrefix, StringComparison.Ordinal) || key.Length > MaxSecretLength)
        {
            return null;
        }

        var hash = Hash(key);
        var device = await dbContext.ScannerDevices.SingleOrDefaultAsync(item => item.KeyHash == hash, cancellationToken);
        if (device is null || device.RevokedAt is not null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (device.LastSeenAt is null || now - device.LastSeenAt.Value >= LastSeenInterval)
        {
            device.LastSeenAt = now;
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Revoked between the read and the write ("RevokedAt" IS NULL failed).
                dbContext.ChangeTracker.Clear();
                return null;
            }
        }

        dbContext.Entry(device).State = EntityState.Detached;
        return device;
    }

    private static (string Code, ScannerDeviceSetupCodeEntity Row) NewCode(Guid deviceId, string createdBy, DateTimeOffset now)
    {
        var code = RandomSecret();
        return (code, new ScannerDeviceSetupCodeEntity
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            CodeHash = Hash(code),
            ExpiresAt = now + SetupCodeLifetime,
            CreatedBy = createdBy,
            CreatedAt = now
        });
    }

    private static string RandomSecret()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}

public enum ScannerDeviceState
{
    Pending,
    Active,
    Revoked,
}

public enum CreateDeviceOutcome
{
    Created,
    NameTaken,
}

public enum NewSetupCodeOutcome
{
    Created,
    NotFound,
    Revoked,
}

public enum RevokeOutcome
{
    Revoked,
    NotFound,
}

/// <summary>A setup code in plain text: only in the response that made it, never stored.</summary>
public sealed record IssuedSetupCode(ScannerDeviceEntity Device, string Code, DateTimeOffset ExpiresAt);

public sealed record CreateDeviceResult(CreateDeviceOutcome Outcome, IssuedSetupCode? Issued);

public sealed record NewSetupCodeResult(NewSetupCodeOutcome Outcome, IssuedSetupCode? Issued);

public sealed record DeviceEnrolment(Guid DeviceId, string DeviceName, string DeviceKey);
