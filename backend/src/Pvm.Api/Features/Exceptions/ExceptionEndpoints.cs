using Pvm.Api.Auth;
using Pvm.Application.Exceptions;
using Pvm.Infrastructure.Exceptions;

namespace Pvm.Api.Features.Exceptions;

public static class ExceptionEndpoints
{
    public static IEndpointRouteBuilder MapExceptionEndpoints(this IEndpointRouteBuilder app)
    {
        var read = app.MapGroup("/api/exceptions")
            .RequireAuthorization("Invoices.Read");
        read.MapGet("/", ListAsync);
        read.MapGet("/{id:guid}", GetAsync);
        read.MapPost("/refresh", RefreshAsync);

        var admin = app.MapGroup("/api/exceptions")
            .RequireAuthorization("Admin");
        admin.MapPost("/{id:guid}/assign", AssignAsync);
        admin.MapPost("/{id:guid}/comments", CommentAsync);
        admin.MapPost("/{id:guid}/status", SetStatusAsync);
        admin.MapPost("/ambiguous/{submissionOperationId:guid}/resolve", ResolveAmbiguousAsync);
        admin.MapPost("/invoices/{invoiceCandidateId:guid}/hold", HoldAsync);
        admin.MapPost("/invoices/{invoiceCandidateId:guid}/release", ReleaseAsync);
        admin.MapPost("/invoices/{invoiceCandidateId:guid}/retry", RetryAsync);
        admin.MapPost("/dead-letters/{deliveryId:guid}/replay", ReplayAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(
        ExceptionOperationsService service,
        CancellationToken cancellationToken,
        string? status = null,
        string? category = null)
    {
        var now = DateTimeOffset.UtcNow;
        await service.SynchronizeAsync(now, cancellationToken);
        var listing = await service.ListAsync(status, category, now, cancellationToken);
        return Results.Ok(listing);
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        ExceptionOperationsService service,
        CancellationToken cancellationToken)
    {
        var task = await service.GetAsync(id, DateTimeOffset.UtcNow, cancellationToken);
        return task is null
            ? Results.NotFound(new { id, message = "Exception task not found." })
            : Results.Ok(task);
    }

    private static async Task<IResult> RefreshAsync(
        ExceptionOperationsService service,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var derived = await service.SynchronizeAsync(now, cancellationToken);
        var listing = await service.ListAsync(null, null, now, cancellationToken);
        return Results.Ok(new { derived, listing.Summary });
    }

    private static async Task<IResult> AssignAsync(
        Guid id,
        AssignExceptionRequest request,
        ExceptionOperationsService service,
        CurrentAppUserAccessor currentUser,
        CancellationToken cancellationToken)
        => Respond(await service.AssignAsync(
            id,
            request.Owner,
            Actor(currentUser),
            DateTimeOffset.UtcNow,
            cancellationToken));

    private static async Task<IResult> CommentAsync(
        Guid id,
        ExceptionCommentRequest request,
        ExceptionOperationsService service,
        CurrentAppUserAccessor currentUser,
        CancellationToken cancellationToken)
        => Respond(await service.AddCommentAsync(
            id,
            Actor(currentUser),
            request.Body,
            DateTimeOffset.UtcNow,
            cancellationToken));

    private static async Task<IResult> SetStatusAsync(
        Guid id,
        ExceptionStatusRequest request,
        ExceptionOperationsService service,
        CurrentAppUserAccessor currentUser,
        CancellationToken cancellationToken)
        => Respond(await service.SetStatusAsync(
            id,
            request.Status,
            Actor(currentUser),
            request.Reason,
            DateTimeOffset.UtcNow,
            cancellationToken));

    private static async Task<IResult> ResolveAmbiguousAsync(
        Guid submissionOperationId,
        ResolveAmbiguousRequest request,
        ExceptionOperationsService service,
        CurrentAppUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<AmbiguousResolutionOutcome>(request.Outcome, ignoreCase: true, out var outcome))
        {
            return Results.BadRequest(new { message = "Choose confirmed accepted, confirmed not accepted, or still unknown." });
        }

        return Respond(await service.ResolveAmbiguousAsync(
            submissionOperationId,
            outcome,
            request.Evidence,
            request.Reason,
            Actor(currentUser),
            DateTimeOffset.UtcNow,
            cancellationToken));
    }

    private static async Task<IResult> HoldAsync(
        Guid invoiceCandidateId,
        ExceptionReasonRequest request,
        ExceptionOperationsService service,
        CurrentAppUserAccessor currentUser,
        CancellationToken cancellationToken)
        => Respond(await service.HoldInvoiceAsync(
            invoiceCandidateId,
            request.Reason,
            Actor(currentUser),
            DateTimeOffset.UtcNow,
            cancellationToken));

    private static async Task<IResult> ReleaseAsync(
        Guid invoiceCandidateId,
        ExceptionReasonRequest request,
        ExceptionOperationsService service,
        CurrentAppUserAccessor currentUser,
        CancellationToken cancellationToken)
        => Respond(await service.ReleaseInvoiceAsync(
            invoiceCandidateId,
            request.Reason,
            Actor(currentUser),
            DateTimeOffset.UtcNow,
            cancellationToken));

    private static async Task<IResult> RetryAsync(
        Guid invoiceCandidateId,
        ExceptionReasonRequest request,
        ExceptionOperationsService service,
        CurrentAppUserAccessor currentUser,
        CancellationToken cancellationToken)
        => Respond(await service.RetryRejectedAsync(
            invoiceCandidateId,
            request.Reason,
            Actor(currentUser),
            DateTimeOffset.UtcNow,
            cancellationToken));

    private static async Task<IResult> ReplayAsync(
        Guid deliveryId,
        ExceptionReasonRequest request,
        ExceptionOperationsService service,
        CurrentAppUserAccessor currentUser,
        CancellationToken cancellationToken)
        => Respond(await service.ReplayDeadLetterAsync(
            deliveryId,
            request.Reason,
            Actor(currentUser),
            DateTimeOffset.UtcNow,
            cancellationToken));

    private static IResult Respond(ExceptionOperationResult result)
    {
        if (result.Applied)
        {
            return Results.Ok(new { applied = true, result.Detail, result.Task });
        }

        return result.Conflict
            ? Results.Conflict(new { message = result.Error, state = result.Detail })
            : Results.BadRequest(new { message = result.Error, state = result.Detail });
    }

    private static string Actor(CurrentAppUserAccessor currentUser)
        => currentUser.User?.Email ?? "unknown";
}

public sealed record AssignExceptionRequest(string? Owner);

public sealed record ExceptionCommentRequest(string Body);

public sealed record ExceptionStatusRequest(string Status, string Reason);

public sealed record ExceptionReasonRequest(string Reason);

public sealed record ResolveAmbiguousRequest(string Outcome, string Evidence, string Reason);
