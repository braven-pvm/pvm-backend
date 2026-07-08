using System.Security.Cryptography;
using System.Text;
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
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

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
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

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
        PvmDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

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

        var now = DateTimeOffset.UtcNow;
        var orderNumbers = batch.Orders
            .Select(order => order.PurchaseOrderNumber)
            .Where(orderNumber => !string.IsNullOrWhiteSpace(orderNumber))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var existing = await dbContext.ShopritePurchaseOrders
            .Include(order => order.Lines)
            .Where(order => orderNumbers.Contains(order.PurchaseOrderNumber))
            .ToDictionaryAsync(order => order.PurchaseOrderNumber, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var created = 0;
        var updated = 0;
        var skipped = 0;

        foreach (var sourceOrder in batch.Orders)
        {
            if (string.IsNullOrWhiteSpace(sourceOrder.PurchaseOrderNumber))
            {
                skipped++;
                continue;
            }

            if (!existing.TryGetValue(sourceOrder.PurchaseOrderNumber, out var entity))
            {
                entity = new ShopritePurchaseOrderEntity
                {
                    Id = Guid.NewGuid(),
                    PurchaseOrderNumber = sourceOrder.PurchaseOrderNumber,
                    DeliveryLocationSource = sourceOrder.DeliveryLocationSource,
                    FirstSeenAt = now,
                    LastSeenAt = now
                };

                dbContext.ShopritePurchaseOrders.Add(entity);
                existing[sourceOrder.PurchaseOrderNumber] = entity;
                created++;
            }
            else
            {
                dbContext.ShopritePurchaseOrderLines.RemoveRange(entity.Lines);
                entity.Lines.Clear();
                updated++;
            }

            Apply(sourceOrder, entity, now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new PurchaseOrderRefreshResponse(
            Received: batch.Orders.Count,
            Created: created,
            Updated: updated,
            Skipped: skipped,
            RefreshedAt: now));
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

    private static void Apply(
        ShopritePurchaseOrder source,
        ShopritePurchaseOrderEntity entity,
        DateTimeOffset seenAt)
    {
        entity.OrderHeaderId = source.OrderHeaderId;
        entity.OrderTypeCode = source.OrderTypeCode;
        entity.OrderTypeLabel = source.OrderTypeLabel;
        entity.SupplierGln = source.SupplierGln;
        entity.BuyerGln = source.BuyerGln;
        entity.DeliveryGln = source.DeliveryGln;
        entity.DeliveryLocationCode = source.DeliveryLocationCode;
        entity.DeliveryLocationName = source.DeliveryLocationName;
        entity.DeliveryLocationSource = source.DeliveryLocationSource;
        entity.CurrencyCode = source.CurrencyCode;
        entity.TotalExcludingTax = source.TotalExcludingTax;
        entity.TotalIncludingTax = source.TotalIncludingTax;
        entity.TotalTax = source.TotalTax;
        entity.PayloadHash = Sha256(source.RawOrderJson);
        entity.RawOrderJson = source.RawOrderJson;
        entity.ShopriteCreatedAt = source.CreatedAt;
        entity.ShopriteLastUpdatedAt = source.LastUpdatedAt;
        entity.LastSeenAt = seenAt;

        entity.Lines.Clear();
        foreach (var line in source.Lines)
        {
            entity.Lines.Add(new ShopritePurchaseOrderLineEntity
            {
                Id = Guid.NewGuid(),
                LineNumber = line.LineNumber,
                Gtin = line.Gtin,
                BuyerItemId = line.BuyerItemId,
                BuyerItemDescription = line.BuyerItemDescription,
                SupplierItemId = line.SupplierItemId,
                Description = line.Description,
                RequestedQuantity = line.RequestedQuantity,
                MeasurementUnitCode = line.MeasurementUnitCode,
                NetAmount = line.NetAmount,
                NetPrice = line.NetPrice,
                MonetaryAmountExcludingTaxes = line.MonetaryAmountExcludingTaxes,
                MonetaryAmountIncludingTaxes = line.MonetaryAmountIncludingTaxes
            });
        }
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
                && candidate.Status is not "Submitted" and not "Ambiguous",
            candidate.UpdatedAt);
    }

    private static T? Deserialize<T>(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? default
            : JsonSerializer.Deserialize<T>(json, SerializerOptions);

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
