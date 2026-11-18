namespace Pvm.Application.Reporting.SellOut;

/// <summary>A GTIN (barcode) of an Acumatica stock item, from its cross references.</summary>
public sealed record ProductGtin(string Gtin, string InventoryId, string? Uom);

/// <summary>Reads the GTINs of all Acumatica stock items.</summary>
public interface IProductGtinSource
{
    Task<IReadOnlyList<ProductGtin>> ReadAllAsync(CancellationToken cancellationToken);
}
