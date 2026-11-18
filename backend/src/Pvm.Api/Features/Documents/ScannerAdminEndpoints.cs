using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pvm.Api.Auth;
using Pvm.Infrastructure.Documents;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Api.Features.Documents;

/// <summary>
/// Documents &gt; Scanner devices in the workbench (design 6.10), Admin only:
/// <list type="bullet">
///   <item><c>GET /api/scanner-devices</c>, <c>POST /api/scanner-devices</c> — list and add devices;
///   adding returns the one-time setup code and its QR payload.</item>
///   <item><c>POST /api/scanner-devices/{id}/setup-code</c> — a new code (re-enrolment); earlier unused codes stop working.</item>
///   <item><c>POST /api/scanner-devices/{id}/revoke</c> — the device's key stops working.</item>
///   <item><c>GET/POST /api/scanner-staff</c>, <c>PUT /api/scanner-staff/{id}</c> — the staff names.</item>
/// </list>
/// </summary>
public static class ScannerAdminEndpoints
{
    public const string NotConfiguredDetail = "Scanner setup is not configured: set DocumentCapture:DeviceApiBaseUrl";

    public static IEndpointRouteBuilder MapScannerAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var devices = app.MapGroup("/api/scanner-devices").RequireAuthorization("Admin");
        devices.MapGet("", ListDevicesAsync);
        devices.MapPost("", CreateDeviceAsync);
        devices.MapPost("/{id:guid}/setup-code", NewSetupCodeAsync);
        devices.MapPost("/{id:guid}/revoke", RevokeAsync);

        var staff = app.MapGroup("/api/scanner-staff").RequireAuthorization("Admin");
        staff.MapGet("", ListStaffAsync);
        staff.MapPost("", CreateStaffAsync);
        staff.MapPut("/{id:guid}", UpdateStaffAsync);

        return app;
    }

    internal static object DeviceView(ScannerDeviceEntity device) => new
    {
        id = device.Id,
        name = device.Name,
        state = ScannerDeviceService.StateOf(device).ToString(),
        createdAt = device.CreatedAt,
        enrolledAt = device.EnrolledAt,
        lastSeenAt = device.LastSeenAt,
        revokedAt = device.RevokedAt
    };

    private static async Task<IResult> ListDevicesAsync(ScannerDeviceService service, CancellationToken cancellationToken)
        => Results.Ok((await service.ListAsync(cancellationToken)).Select(DeviceView));

    private static async Task<IResult> CreateDeviceAsync(
        NameRequest? request,
        ScannerDeviceService service,
        CurrentAppUserAccessor currentUser,
        IOptions<DocumentCaptureOptions> options,
        CancellationToken cancellationToken)
    {
        if (!options.Value.TryGetDeviceApiBaseUrl(out var baseUrl))
        {
            return NotConfigured();
        }

        var name = request?.Name?.Trim();
        if (!ValidName(name))
        {
            return BadRequest($"'name' is required, at most {ScannerDeviceService.MaxNameLength} characters.");
        }

        var user = DocumentReviewEndpoints.CurrentEmail(currentUser);
        if (user is null)
        {
            return DocumentReviewEndpoints.UserNotResolved();
        }

        var result = await service.CreateAsync(name!, user, cancellationToken);
        return result.Outcome switch
        {
            // 201 without Location: there is no GET for one device; the list holds it.
            CreateDeviceOutcome.Created => Results.Created((string?)null, SetupCodeView(result.Issued!, baseUrl)),
            CreateDeviceOutcome.NameTaken => Results.Problem(
                $"A device named '{name}' already exists.",
                statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException("Unknown create-device outcome.")
        };
    }

    private static async Task<IResult> NewSetupCodeAsync(
        Guid id,
        ScannerDeviceService service,
        CurrentAppUserAccessor currentUser,
        IOptions<DocumentCaptureOptions> options,
        CancellationToken cancellationToken)
    {
        if (!options.Value.TryGetDeviceApiBaseUrl(out var baseUrl))
        {
            return NotConfigured();
        }

        var user = DocumentReviewEndpoints.CurrentEmail(currentUser);
        if (user is null)
        {
            return DocumentReviewEndpoints.UserNotResolved();
        }

        var result = await service.NewSetupCodeAsync(id, user, cancellationToken);
        return result.Outcome switch
        {
            NewSetupCodeOutcome.Created => Results.Ok(SetupCodeView(result.Issued!, baseUrl)),
            NewSetupCodeOutcome.NotFound => DeviceNotFound(),
            NewSetupCodeOutcome.Revoked => Results.Problem(
                "The device is revoked; add it again as a new device.",
                statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException("Unknown setup-code outcome.")
        };
    }

    private static async Task<IResult> RevokeAsync(
        Guid id,
        ScannerDeviceService service,
        CurrentAppUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var user = DocumentReviewEndpoints.CurrentEmail(currentUser);
        if (user is null)
        {
            return DocumentReviewEndpoints.UserNotResolved();
        }

        if (await service.RevokeAsync(id, user, cancellationToken) == RevokeOutcome.NotFound)
        {
            return DeviceNotFound();
        }

        var device = await service.FindAsync(id, cancellationToken);
        return Results.Ok(DeviceView(device!));
    }

    private static async Task<IResult> ListStaffAsync(PvmDbContext dbContext, CancellationToken cancellationToken)
    {
        var staff = await dbContext.ScannerStaff
            .AsNoTracking()
            .OrderByDescending(item => item.Active)
            .ThenBy(item => item.Name)
            .ToListAsync(cancellationToken);
        return Results.Ok(staff.Select(StaffView));
    }

    private static async Task<IResult> CreateStaffAsync(
        NameRequest? request,
        PvmDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var name = request?.Name?.Trim();
        if (!ValidName(name))
        {
            return BadRequest($"'name' is required, at most {ScannerDeviceService.MaxNameLength} characters.");
        }

        var now = DateTimeOffset.UtcNow;
        var staff = new ScannerStaffEntity
        {
            Id = Guid.NewGuid(),
            Name = name!,
            Active = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.ScannerStaff.Add(staff);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/scanner-staff/{staff.Id:D}", StaffView(staff));
    }

    private static async Task<IResult> UpdateStaffAsync(
        Guid id,
        UpdateStaffRequest? request,
        PvmDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var name = request?.Name?.Trim();
        if (!ValidName(name) || request?.Active is null)
        {
            return BadRequest($"'name' (at most {ScannerDeviceService.MaxNameLength} characters) and 'active' are required.");
        }

        var staff = await dbContext.ScannerStaff.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (staff is null)
        {
            return Results.Problem("The staff name was not found.", statusCode: StatusCodes.Status404NotFound);
        }

        staff.Name = name!;
        staff.Active = request.Active.Value;
        staff.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(StaffView(staff));
    }

    private static object SetupCodeView(IssuedSetupCode issued, string baseUrl) => new
    {
        device = DeviceView(issued.Device),
        setupCode = issued.Code,
        expiresAt = issued.ExpiresAt,
        qrPayload = JsonSerializer.Serialize(new QrPayload(1, baseUrl, issued.Code))
    };

    private static object StaffView(ScannerStaffEntity staff) => new
    {
        id = staff.Id,
        name = staff.Name,
        active = staff.Active,
        createdAt = staff.CreatedAt,
        updatedAt = staff.UpdatedAt
    };

    private static bool ValidName(string? name)
        => !string.IsNullOrEmpty(name) && name.Length <= ScannerDeviceService.MaxNameLength;

    private static IResult NotConfigured()
        => Results.Problem(NotConfiguredDetail, statusCode: StatusCodes.Status500InternalServerError);

    private static IResult DeviceNotFound()
        => Results.Problem("The scanner device was not found.", statusCode: StatusCodes.Status404NotFound);

    private static IResult BadRequest(string detail)
        => Results.Problem(detail, statusCode: StatusCodes.Status400BadRequest);

    private sealed record QrPayload(
        [property: System.Text.Json.Serialization.JsonPropertyName("v")] int V,
        [property: System.Text.Json.Serialization.JsonPropertyName("api")] string Api,
        [property: System.Text.Json.Serialization.JsonPropertyName("code")] string Code);

    public sealed record NameRequest(string? Name);

    public sealed record UpdateStaffRequest(string? Name, bool? Active);
}
