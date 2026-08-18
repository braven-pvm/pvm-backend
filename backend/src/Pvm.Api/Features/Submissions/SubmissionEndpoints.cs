using System.Text.Json;
using Pvm.Api.Auth;
using Pvm.Application.Submissions;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Api.Features.Submissions;

public static class SubmissionEndpoints
{
    public static IEndpointRouteBuilder MapSubmissionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/invoices");

        group.MapPost("/{id:guid}/submit", SubmitInvoiceAsync)
            .RequireAuthorization("Invoices.Write");
        group.MapGet("/{id:guid}/attempts", async (
            Guid id,
            PvmDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var attempts = dbContext.InvoiceSubmissionAttempts
                .Where(attempt => attempt.InvoiceCandidateId == id)
                .OrderByDescending(attempt => attempt.CreatedAt)
                .Select(attempt => new
                {
                    attempt.Id,
                    attempt.InitiatedBy,
                    attempt.InitiationMode,
                    attempt.Status,
                    attempt.ResponseStatusCode,
                    attempt.ErrorMessage,
                    attempt.CreatedAt
                });

            return Results.Ok(attempts);
        }).RequireAuthorization("Invoices.Read");

        return app;
    }

    private static async Task<IResult> SubmitInvoiceAsync(
        Guid id,
        SubmitShopriteInvoiceHandler handler,
        CurrentAppUserAccessor currentUser,
        PvmDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var actor = currentUser.User?.Email ?? "unknown";
        var result = await handler.HandleAsync(
            new SubmitShopriteInvoiceCommand(Guid.NewGuid(), id, actor, "manual"),
            cancellationToken);

        await RecordRefusalAsync(dbContext, id, actor, result, cancellationToken);

        return result.Status switch
        {
            SubmitShopriteInvoiceStatus.Submitted => Results.Ok(result),
            SubmitShopriteInvoiceStatus.InProgress => Results.Accepted($"/api/invoices/candidates/{id}", result),
            SubmitShopriteInvoiceStatus.ValidationBlocked => Results.BadRequest(result),
            SubmitShopriteInvoiceStatus.PolicyBlocked => Results.Conflict(result),
            SubmitShopriteInvoiceStatus.DuplicateBlocked => Results.Conflict(result),
            SubmitShopriteInvoiceStatus.ManualReviewRequired => Results.Conflict(result),
            SubmitShopriteInvoiceStatus.Ambiguous => Results.Accepted($"/api/invoices/candidates/{id}", result),
            _ => Results.BadRequest(result)
        };
    }

    private static async Task RecordRefusalAsync(
        PvmDbContext dbContext,
        Guid invoiceCandidateId,
        string actor,
        SubmitShopriteInvoiceResult result,
        CancellationToken cancellationToken)
    {
        if (result.Status is not (SubmitShopriteInvoiceStatus.PolicyBlocked
            or SubmitShopriteInvoiceStatus.ValidationBlocked
            or SubmitShopriteInvoiceStatus.DuplicateBlocked
            or SubmitShopriteInvoiceStatus.ManualReviewRequired))
        {
            return;
        }

        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            Id = Guid.NewGuid(),
            EntityType = "InvoiceCandidate",
            EntityId = invoiceCandidateId.ToString(),
            Action = "manual-submission-refused",
            Actor = actor,
            DetailsJson = JsonSerializer.Serialize(
                new
                {
                    status = result.Status.ToString(),
                    message = result.Message,
                    submissionOperationId = result.SubmissionOperationId
                },
                SerializerOptions),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
}
