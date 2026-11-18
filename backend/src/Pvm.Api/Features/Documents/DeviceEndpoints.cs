using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Pvm.Api.Auth;
using Pvm.Application.Documents;
using Pvm.Infrastructure.Documents;
using Pvm.Infrastructure.Persistence;

namespace Pvm.Api.Features.Documents;

/// <summary>
/// The scanner phone's endpoints (design 6.10):
/// <list type="bullet">
///   <item><c>POST /api/device/enrol</c> — anonymous and rate-limited: a setup code for a device key.</item>
///   <item><c>GET /api/device/config</c> — the device name, document types and active staff names.</item>
///   <item><c>POST /api/device/documents</c> — the <c>POST /api/documents</c> upload, plus an optional <c>staffId</c>.</item>
/// </list>
/// The last two need <c>Authorization: Device &lt;key&gt;</c> (policy <c>Documents.Device</c>).
/// </summary>
public static class DeviceEndpoints
{
    public const string EnrolRateLimitPolicy = "device-enrol";
    public const int EnrolPermitsPerMinute = 10;

    /// <summary>One message for every refused code, so a caller cannot learn which check failed.</summary>
    public const string InvalidCodeDetail = "The setup code is not valid. Ask an admin for a new setup code.";

    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/device");

        group.MapPost("/enrol", EnrolAsync)
            .AllowAnonymous()
            .RequireRateLimiting(EnrolRateLimitPolicy);

        group.MapGet("/config", ConfigAsync)
            .RequireAuthorization(Pvm.Api.Auth.ServiceCollectionExtensions.DocumentsDevicePolicy);

        group.MapPost("/documents", UploadAsync)
            .RequireAuthorization(Pvm.Api.Auth.ServiceCollectionExtensions.DocumentsDevicePolicy)
            .DisableAntiforgery();

        return app;
    }

    /// <summary>
    /// Ten enrol calls per minute per client IP; the eleventh gets 429. Other limiters keep their
    /// own rejection status.
    /// </summary>
    public static RateLimiterOptions AddDeviceEnrolRateLimit(this RateLimiterOptions options)
    {
        options.AddPolicy(EnrolRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = EnrolPermitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

        var earlier = options.OnRejected;
        options.OnRejected = async (context, cancellationToken) =>
        {
            var policy = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
            if (policy == EnrolRateLimitPolicy)
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                return;
            }

            if (earlier is not null)
            {
                await earlier(context, cancellationToken);
            }
        };
        return options;
    }

    private static async Task<IResult> EnrolAsync(
        EnrolRequest? request,
        ScannerDeviceService service,
        CancellationToken cancellationToken)
    {
        var enrolment = await service.EnrolAsync(request?.Code, cancellationToken);
        return enrolment is null
            ? Results.Problem(InvalidCodeDetail, statusCode: StatusCodes.Status400BadRequest)
            : Results.Ok(new { deviceKey = enrolment.DeviceKey, deviceName = enrolment.DeviceName });
    }

    private static async Task<IResult> ConfigAsync(
        ClaimsPrincipal principal,
        IEnumerable<IDocumentType> documentTypes,
        PvmDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var staff = await dbContext.ScannerStaff
            .AsNoTracking()
            .Where(item => item.Active)
            .OrderBy(item => item.Name)
            .Select(item => new { id = item.Id, name = item.Name })
            .ToListAsync(cancellationToken);

        return Results.Ok(new
        {
            deviceName = principal.FindFirstValue(DeviceKeyAuthenticationHandler.DeviceNameClaim),
            documentTypes = documentTypes.Select(type => new { code = type.Code, displayName = type.DisplayName }),
            staff
        });
    }

    private static Task<IResult> UploadAsync(
        HttpRequest request,
        ClaimsPrincipal principal,
        DocumentIntakeService intake,
        PvmDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var deviceId = Guid.Parse(principal.FindFirstValue(DeviceKeyAuthenticationHandler.DeviceIdClaim)!);
        var deviceName = principal.FindFirstValue(DeviceKeyAuthenticationHandler.DeviceNameClaim)!;

        return intake.UploadAsync(
            request,
            async (form, token) =>
            {
                var raw = form["staffId"].ToString();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return UploaderResolution.Of(new DocumentUploader($"device:{deviceName}", deviceId));
                }

                var staff = Guid.TryParse(raw, out var staffId)
                    ? await dbContext.ScannerStaff
                        .AsNoTracking()
                        .SingleOrDefaultAsync(item => item.Id == staffId && item.Active, token)
                    : null;
                return staff is null
                    ? UploaderResolution.Refuse(DocumentIntakeService.BadRequest(
                        "The form field 'staffId' must be the ID of an active staff name."))
                    : UploaderResolution.Of(new DocumentUploader($"device:{deviceName}", deviceId, staff.Id, staff.Name));
            },
            cancellationToken);
    }

    public sealed record EnrolRequest(string? Code);
}
