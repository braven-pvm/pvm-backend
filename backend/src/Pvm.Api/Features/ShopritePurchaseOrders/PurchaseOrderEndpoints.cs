using System.Text.Json;
using Pvm.Api.Features.Invoices.Models;
using Microsoft.EntityFrameworkCore;
using Pvm.Api.Auth;
using Pvm.Application.Messaging;
using Pvm.Domain.Validation;
using Pvm.Infrastructure.Operations;
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
        group.MapGet("/freshness", GetFreshnessAsync)
            .RequireAuthorization("Invoices.Read");
        group.MapPost("/refresh", RefreshPurchaseOrdersAsync)
            .RequireAuthorization("Invoices.Write");
        group.MapPost("/{id:guid}/seed-test-invoice", SeedTestInvoiceAsync)
            .RequireAuthorization("Invoices.Write");
        group.MapPost("/reset", ResetPurchaseOrdersAsync)
            .RequireAuthorization("Admin");

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
        ShopritePurchaseOrderRefreshRunQueue runQueue,
        CurrentAppUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var queued = await runQueue.EnqueueAsync(
            IntegrationRunTriggers.Manual,
            currentUser.User?.Email ?? "unknown",
            scheduleKey: null,
            cancellationToken);
        var statusUrl = $"/api/integration-runs/{queued.RunId:D}";
        return Results.Accepted(
            statusUrl,
            new PurchaseOrderRefreshResponse(
                queued.RunId,
                queued.MessageId,
                queued.Created,
                statusUrl));
    }

    private static async Task<IResult> GetFreshnessAsync(
        ShopritePurchaseOrderFreshnessService freshnessService,
        CancellationToken cancellationToken)
        => Results.Ok(await freshnessService.GetAsync(DateTimeOffset.UtcNow, cancellationToken));

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

    private static async Task<IResult> ResetPurchaseOrdersAsync(
        ResetPurchaseOrdersRequest request,
        ShopriteOrderAcknowledgementService acknowledgementService,
        PvmDbContext dbContext,
        CurrentAppUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.BadRequest(new { message = "A reason is required to reset Shoprite orders." });
        }

        var orderNumbers = (request.PurchaseOrderNumbers ?? [])
            .Where(number => !string.IsNullOrWhiteSpace(number))
            .Select(number => number.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (orderNumbers.Length == 0)
        {
            return Results.BadRequest(new { message = "At least one Shoprite order number is required." });
        }

        var actor = currentUser.User?.Email ?? "unknown";
        int reset;
        try
        {
            reset = await acknowledgementService.ResetAsync(orderNumbers, cancellationToken);
        }
        catch (Exception exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway);
        }

        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            Id = Guid.NewGuid(),
            EntityType = "ShopritePurchaseOrder",
            EntityId = string.Join(",", orderNumbers.Take(20)),
            Action = "shoprite-orders-reset",
            Actor = actor,
            DetailsJson = JsonSerializer.Serialize(
                new { reason = request.Reason, orderNumbers, localOrdersUpdated = reset },
                SerializerOptions),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { requested = orderNumbers.Length, localOrdersUpdated = reset });
    }

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
            order.AcknowledgedAt,
            order.AcknowledgementAttempts,
            order.LastAcknowledgementError,
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
