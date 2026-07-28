using System.Text.Json;
using Pvm.Api.Features.Invoices.Models;
using Microsoft.EntityFrameworkCore;
using Pvm.Application.Shoprite;
using Pvm.Domain.Validation;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Pvm.Infrastructure.Shoprite;

namespace Pvm.Api.Features.ShopritePurchaseOrders;

public static class PurchaseOrderEndpoints
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapPurchaseOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/shoprite/purchase-orders");

        group.MapGet("/", ListPurchaseOrdersAsync)
            .RequireAuthorization("Invoices.Read");
        group.MapGet("/{id:guid}", GetPurchaseOrderAsync)
            .RequireAuthorization("Invoices.Read");
        group.MapPost("/refresh", RefreshPurchaseOrdersAsync)
            .RequireAuthorization("Invoices.Write");
        group.MapPost("/{id:guid}/seed-test-invoice", SeedTestInvoiceAsync)
            .RequireAuthorization("Invoices.Write");

        return app;
    }

    private static async Task<IResult> ListPurchaseOrdersAsync(
        PvmDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var orders = await dbContext.ShopritePurchaseOrders
            .AsNoTracking()
            .Include(order => order.Lines)
            .OrderByDescending(order => order.LastSeenAt)
            .ToListAsync(cancellationToken);

        return Results.Ok(orders.Select(ToSummaryResponse).ToArray());
    }

    private static async Task<IResult> GetPurchaseOrderAsync(
        Guid id,
        PvmDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var order = await dbContext.ShopritePurchaseOrders
            .AsNoTracking()
            .Include(order => order.Lines)
            .SingleOrDefaultAsync(order => order.Id == id, cancellationToken);

        if (order is null)
        {
            return Results.NotFound(new { id, message = "Shoprite purchase order not found." });
        }

        var linkedCandidates = await dbContext.InvoiceCandidates
            .AsNoTracking()
            .Where(candidate => candidate.MatchedShopritePurchaseOrderId == order.Id)
            .OrderByDescending(candidate => candidate.UpdatedAt)
            .ToListAsync(cancellationToken);

        return Results.Ok(ToDetailResponse(order, linkedCandidates));
    }

    private static async Task<IResult> RefreshPurchaseOrdersAsync(
        IShopritePurchaseOrderClient purchaseOrderClient,
        ShopritePurchaseOrderRefreshService purchaseOrderRefreshService,
        PvmDbContext dbContext,
        CancellationToken cancellationToken)
    {
        ShopritePurchaseOrderBatch batch;
        try
        {
            batch = await purchaseOrderClient.FetchAsync(cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway);
        }
        catch (HttpRequestException)
        {
            return Results.Problem("Shoprite VendorOrder request failed.", statusCode: StatusCodes.Status502BadGateway);
        }

        var result = await purchaseOrderRefreshService.RefreshAsync(
            batch,
            DateTimeOffset.UtcNow,
            cancellationToken);

        return Results.Ok(new PurchaseOrderRefreshResponse(
            result.Received,
            result.Created,
            result.Updated,
            result.Skipped,
            result.RefreshedAt));
    }

    private static async Task<IResult> SeedTestInvoiceAsync(
        Guid id,
        ShopriteSeedInvoiceCandidateService seedInvoiceCandidateService,
        CancellationToken cancellationToken)
    {
        var candidate = await seedInvoiceCandidateService.SeedAsync(
            id,
            DateTimeOffset.UtcNow,
            cancellationToken);

        if (candidate is null)
        {
            return Results.NotFound(new { id, message = "Shoprite purchase order not found." });
        }

        return Results.Ok(ToInvoiceCandidateSummaryResponse(candidate));
    }

    private static PurchaseOrderSummaryResponse ToSummaryResponse(ShopritePurchaseOrderEntity order)
        => new(
            order.Id,
            order.PurchaseOrderNumber,
            order.OrderTypeCode,
            order.OrderTypeLabel,
            order.DeliveryGln,
            order.DeliveryLocationCode,
            order.DeliveryLocationName,
            order.DeliveryLocationSource,
            order.SupplierGln,
            order.Lines.Count,
            order.LastSeenAt);

    private static PurchaseOrderDetailResponse ToDetailResponse(
        ShopritePurchaseOrderEntity order,
        IReadOnlyList<InvoiceCandidateEntity> linkedCandidates)
        => new(
            order.Id,
            order.PurchaseOrderNumber,
            order.OrderHeaderId,
            order.OrderTypeCode,
            order.OrderTypeLabel,
            order.SupplierGln,
            order.BuyerGln,
            order.DeliveryGln,
            order.DeliveryLocationCode,
            order.DeliveryLocationName,
            order.DeliveryLocationSource,
            order.CurrencyCode,
            order.TotalExcludingTax,
            order.TotalIncludingTax,
            order.TotalTax,
            order.SourceEnvironment,
            order.SourceEndpoint,
            order.PayloadHash,
            order.ShopriteCreatedAt,
            order.ShopriteLastUpdatedAt,
            order.FirstSeenAt,
            order.LastSeenAt,
            order.Lines.OrderBy(line => line.LineNumber).Select(ToLineResponse).ToArray(),
            linkedCandidates.Select(candidate => new LinkedInvoiceCandidateResponse(
                candidate.Id,
                candidate.InvoiceNumber,
                candidate.CustomerAccount,
                candidate.Status,
                candidate.UpdatedAt)).ToArray(),
            order.RawOrderJson);

    private static PurchaseOrderLineResponse ToLineResponse(ShopritePurchaseOrderLineEntity line)
        => new(
            line.Id,
            line.LineNumber,
            line.Gtin,
            line.BuyerItemId,
            line.BuyerItemDescription,
            line.SupplierItemId,
            line.Description,
            line.RequestedQuantity,
            line.MeasurementUnitCode,
            line.NetAmount,
            line.NetPrice,
            line.MonetaryAmountExcludingTaxes,
            line.MonetaryAmountIncludingTaxes);

    private static InvoiceCandidateSummaryResponse ToInvoiceCandidateSummaryResponse(InvoiceCandidateEntity candidate)
    {
        var validation = Deserialize<ValidationResult>(candidate.ValidationJson) ?? new ValidationResult([]);

        return new InvoiceCandidateSummaryResponse(
            candidate.Id,
            candidate.InvoiceNumber,
            candidate.CustomerAccount,
            candidate.CustomerLocation,
            candidate.ShopritePurchaseOrderNumber,
            candidate.MatchedShopritePurchaseOrderId,
            string.IsNullOrWhiteSpace(candidate.ShopritePurchaseOrderNumber)
                ? "MissingPoNumber"
                : candidate.MatchedShopritePurchaseOrderId is null ? "Unmatched" : "Matched",
            candidate.StoreDcGln,
            candidate.Status,
            validation.CanSubmit
                && candidate.MatchedShopritePurchaseOrderId is not null
                && candidate.Status is not "Submitted" and not "Rejected" and not "Ambiguous",
            candidate.UpdatedAt);
    }

    private static T? Deserialize<T>(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? default
            : JsonSerializer.Deserialize<T>(json, SerializerOptions);

}
