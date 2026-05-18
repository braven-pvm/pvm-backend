namespace Pvm.Api.Features.Invoices;

public static class InvoiceEndpoints
{
    public static IEndpointRouteBuilder MapInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/invoices");

        group.MapGet("/candidates", () => Results.Ok(Array.Empty<object>()));
        group.MapGet("/candidates/{id:guid}", (Guid id) => Results.Ok(new { id }));
        group.MapPost("/refresh", () => Results.Accepted());
        group.MapPost("/{id:guid}/revalidate", (Guid id) => Results.Ok(new { id, status = "validated" }));

        return app;
    }
}
