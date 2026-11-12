using Pvm.Infrastructure.Stock;

namespace Pvm.Api.Features.Stock;

/// <summary>
/// Stock-sync endpoints:
/// <list type="bullet">
///   <item><c>GET /api/stock/sync/latest</c> — the latest recorded run of the Acumatica →
///   WooCommerce stock sync, with the decision for each product. 204 when no run is recorded.</item>
/// </list>
/// </summary>
public static class StockSyncEndpoints
{
    public static IEndpointRouteBuilder MapStockSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/stock");

        group.MapGet("/sync/latest", GetLatestAsync)
            .RequireAuthorization("Invoices.Read");

        return app;
    }

    private static async Task<IResult> GetLatestAsync(
        StockSyncRunRecorder recorder,
        CancellationToken cancellationToken)
    {
        var run = await recorder.GetLatestAsync(cancellationToken);
        return run is null ? Results.NoContent() : Results.Ok(run);
    }
}
