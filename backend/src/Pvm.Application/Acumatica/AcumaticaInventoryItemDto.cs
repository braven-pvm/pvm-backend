namespace Pvm.Application.Acumatica;

public sealed record AcumaticaInventoryItemDto(
    string InventoryId,
    string Description,
    string? Status,
    IReadOnlyList<string> UnitsOfMeasure);
