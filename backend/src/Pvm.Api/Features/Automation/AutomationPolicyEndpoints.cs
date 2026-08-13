using Pvm.Api.Auth;
using Pvm.Application.Automation;
using Pvm.Infrastructure.Automation;

namespace Pvm.Api.Features.Automation;

public static class AutomationPolicyEndpoints
{
    public static IEndpointRouteBuilder MapAutomationPolicyEndpoints(this IEndpointRouteBuilder app)
    {
        var read = app.MapGroup("/api/automation")
            .RequireAuthorization("Invoices.Read");
        read.MapGet("/policy", GetAsync);

        var admin = app.MapGroup("/api/automation")
            .RequireAuthorization("Admin");
        admin.MapPut("/policy", ChangeAsync);
        admin.MapPost("/emergency-stop", SetEmergencyStopAsync);
        return app;
    }

    private static async Task<IResult> GetAsync(
        AutomationPolicyService service,
        IConfiguration configuration,
        CancellationToken cancellationToken)
        => Results.Ok(Response(await service.GetViewAsync(cancellationToken), configuration));

    private static async Task<IResult> ChangeAsync(
        ChangeAutomationPolicyRequest request,
        AutomationPolicyService service,
        CurrentAppUserAccessor currentUser,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var environment = configuration["Pvm:EnvironmentName"] ?? "Development";
        var confirmationError = ValidateEnablementConfirmation(request, environment);
        if (confirmationError is not null)
        {
            return Results.BadRequest(new { message = confirmationError });
        }

        var result = await service.ChangeAsync(
            new AutomationPolicyChange(
                request.ExpectedVersion,
                request.Mode,
                request.AccountAllowlist,
                request.LocationAllowlist,
                request.SupportedOrderTypes,
                request.StabilizationDelayMinutes,
                request.PurchaseOrderFreshnessMinutes,
                request.AcumaticaFreshnessMinutes,
                request.DailyAutomaticSubmissionCap,
                request.AutomaticWindowStart,
                request.AutomaticWindowEnd,
                request.TimeZoneId,
                Actor(currentUser),
                request.Reason),
            cancellationToken);
        if (result.Conflict)
        {
            return Results.Conflict(new { message = result.Error, currentPolicy = result.Policy });
        }
        if (result.Error is not null)
        {
            return Results.BadRequest(new { message = result.Error });
        }
        if (result.Changed)
        {
            await service.EvaluateCandidatesAsync(null, DateTimeOffset.UtcNow, cancellationToken);
        }

        return Results.Ok(Response(await service.GetViewAsync(cancellationToken), configuration));
    }

    private static async Task<IResult> SetEmergencyStopAsync(
        EmergencyStopRequest request,
        AutomationPolicyService service,
        CurrentAppUserAccessor currentUser,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var result = await service.SetEmergencyStopAsync(
            request.ExpectedVersion,
            request.Active,
            Actor(currentUser),
            request.Reason,
            cancellationToken);
        if (result.Conflict)
        {
            return Results.Conflict(new { message = result.Error, currentPolicy = result.Policy });
        }
        if (result.Error is not null)
        {
            return Results.BadRequest(new { message = result.Error });
        }
        if (result.Changed)
        {
            await service.EvaluateCandidatesAsync(null, DateTimeOffset.UtcNow, cancellationToken);
        }

        return Results.Ok(Response(await service.GetViewAsync(cancellationToken), configuration));
    }

    private static string? ValidateEnablementConfirmation(ChangeAutomationPolicyRequest request, string environment)
    {
        if (request.Mode != AutomationMode.Enabled)
        {
            return null;
        }
        if (!request.AcknowledgeAutomaticSubmissions)
        {
            return "Confirm that eligible invoices will be submitted automatically.";
        }
        if (!string.Equals(request.EnvironmentConfirmation?.Trim(), environment, StringComparison.OrdinalIgnoreCase))
        {
            return $"Type the environment name '{environment}' to enable unrestricted automation.";
        }
        if (!string.Equals(request.TypedConfirmation?.Trim(), $"ENABLE {environment}", StringComparison.Ordinal))
        {
            return $"Type 'ENABLE {environment}' exactly to enable unrestricted automation.";
        }
        return null;
    }

    private static string Actor(CurrentAppUserAccessor currentUser)
        => currentUser.User?.Email ?? "unknown";

    private static object Response(AutomationPolicyView view, IConfiguration configuration)
        => new
        {
            environmentName = configuration["Pvm:EnvironmentName"] ?? "Development",
            view.Policy,
            view.DecisionSummary,
            view.RecentDecisions,
            view.RecentVersions
        };
}

public sealed record ChangeAutomationPolicyRequest(
    int ExpectedVersion,
    AutomationMode Mode,
    string[] AccountAllowlist,
    string[] LocationAllowlist,
    string[] SupportedOrderTypes,
    int StabilizationDelayMinutes,
    int PurchaseOrderFreshnessMinutes,
    int AcumaticaFreshnessMinutes,
    int DailyAutomaticSubmissionCap,
    TimeOnly AutomaticWindowStart,
    TimeOnly AutomaticWindowEnd,
    string TimeZoneId,
    string Reason,
    bool AcknowledgeAutomaticSubmissions = false,
    string? EnvironmentConfirmation = null,
    string? TypedConfirmation = null);

public sealed record EmergencyStopRequest(int ExpectedVersion, bool Active, string Reason);
