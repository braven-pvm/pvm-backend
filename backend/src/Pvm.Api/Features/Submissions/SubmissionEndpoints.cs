using Pvm.Api.Auth;
using Pvm.Application.Submissions;
using Pvm.Infrastructure.Persistence;

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
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);

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
        PvmDbContext dbContext,
        SubmitShopriteInvoiceHandler handler,
        CurrentAppUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        var result = await handler.HandleAsync(
            new SubmitShopriteInvoiceCommand(id, currentUser.User?.Email ?? "unknown", "manual"),
            cancellationToken);

        return result.Status switch
        {
            SubmitShopriteInvoiceStatus.Submitted => Results.Ok(result),
            SubmitShopriteInvoiceStatus.ValidationBlocked => Results.BadRequest(result),
            SubmitShopriteInvoiceStatus.DuplicateBlocked => Results.Conflict(result),
            SubmitShopriteInvoiceStatus.ManualReviewRequired => Results.Conflict(result),
            SubmitShopriteInvoiceStatus.Ambiguous => Results.Accepted($"/api/invoices/candidates/{id}", result),
            _ => Results.BadRequest(result)
        };
    }
}
