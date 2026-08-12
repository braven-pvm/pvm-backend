using Pvm.Api.Auth;
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
        group.MapPut("/{inventoryId}/{acumaticaUom}", SaveAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(
        string? search,
        ShopriteInventoryMappingService mappingService,
        CancellationToken cancellationToken)
        => Results.Ok(await mappingService.ListAsync(search, cancellationToken));

    private static async Task<IResult> SaveAsync(
        string inventoryId,
        string acumaticaUom,
        SaveInventoryMappingRequest request,
        CurrentAppUserAccessor currentUser,
        ShopriteInventoryMappingService mappingService,
        CancellationToken cancellationToken)
    {
        if (currentUser.User is null)
        {
            return Results.Unauthorized();
        }

        var result = await mappingService.SaveAsync(
            inventoryId,
            acumaticaUom,
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
