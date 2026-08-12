using Pvm.Api.Auth;
using Pvm.Application.Acumatica;
using Pvm.Domain.Invoices;
using Pvm.Infrastructure.Shoprite;

namespace Pvm.Api.Features.Mappings;

public static class InventoryMappingEndpoints
{
    public static IEndpointRouteBuilder MapInventoryMappingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/inventory-mappings")
            .RequireAuthorization("Admin");

        group.MapGet("/", ListAsync);
        group.MapGet("/shoprite-items", ListShopriteItemsAsync);
        group.MapGet("/acumatica-items/{inventoryId}", GetAcumaticaItemAsync);
        group.MapPut("/{inventoryId}/{acumaticaUom}", SaveAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(
        string? search,
        ShopriteInventoryMappingService mappingService,
        CancellationToken cancellationToken)
        => Results.Ok(await mappingService.ListAsync(search, cancellationToken));

    private static async Task<IResult> ListShopriteItemsAsync(
        string? search,
        ShopriteInventoryMappingService mappingService,
        CancellationToken cancellationToken)
        => Results.Ok(await mappingService.ListShopriteCatalogAsync(search, cancellationToken));

    private static async Task<IResult> GetAcumaticaItemAsync(
        string inventoryId,
        IAcumaticaInvoiceClient acumaticaClient,
        CancellationToken cancellationToken)
    {
        var item = await acumaticaClient.FetchInventoryItemAsync(inventoryId, cancellationToken);
        return item is null ? Results.NotFound(new { message = "Acumatica inventory item was not found." }) : Results.Ok(item);
    }

    private static async Task<IResult> SaveAsync(
        string inventoryId,
        string acumaticaUom,
        SaveInventoryMappingRequest request,
        CurrentAppUserAccessor currentUser,
        IAcumaticaInvoiceClient acumaticaClient,
        ShopriteInventoryMappingService mappingService,
        CancellationToken cancellationToken)
    {
        if (currentUser.User is null)
        {
            return Results.Unauthorized();
        }

        var inventoryItem = await acumaticaClient.FetchInventoryItemAsync(inventoryId, cancellationToken);
        if (inventoryItem is null)
        {
            return Results.BadRequest(new { message = "Acumatica inventory item was not found." });
        }

        var normalizedUom = ShopriteInventoryMappingService.Normalize(acumaticaUom);
        if (!inventoryItem.UnitsOfMeasure.Contains(normalizedUom, StringComparer.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new
            {
                message = $"Acumatica UOM '{normalizedUom}' is not configured for {inventoryItem.InventoryId}.",
                availableUnitsOfMeasure = inventoryItem.UnitsOfMeasure
            });
        }

        var result = await mappingService.SaveAsync(
            inventoryItem.InventoryId,
            normalizedUom,
            request.PurchaseOrderLineId,
            request.ShopriteUom,
            currentUser.User.Email,
            request.Reason,
            cancellationToken);

        return result.Status switch
        {
            ShopriteInventoryMappingSaveStatus.Saved => Results.Ok(result),
            ShopriteInventoryMappingSaveStatus.PurchaseOrderLineNotFound => Results.NotFound(result),
            _ => Results.BadRequest(result)
        };
    }
}

public sealed record SaveInventoryMappingRequest(
    Guid PurchaseOrderLineId,
    ShopriteMeasurementUnit ShopriteUom,
    string Reason);
