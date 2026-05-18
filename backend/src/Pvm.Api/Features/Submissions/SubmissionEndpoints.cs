namespace Pvm.Api.Features.Submissions;

public static class SubmissionEndpoints
{
    public static IEndpointRouteBuilder MapSubmissionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/invoices");

        group.MapPost("/{id:guid}/submit", (Guid id) => Results.Accepted($"/api/invoices/candidates/{id}", new { id, status = "queued" }));
        group.MapGet("/{id:guid}/attempts", (Guid id) => Results.Ok(Array.Empty<object>()));

        return app;
    }
}
