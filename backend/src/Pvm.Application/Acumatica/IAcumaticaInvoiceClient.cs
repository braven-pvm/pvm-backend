namespace Pvm.Application.Acumatica;

public interface IAcumaticaInvoiceClient
{
    Task<IReadOnlyList<AcumaticaInvoiceDto>> FetchFinalizedInvoicesAsync(
        CancellationToken cancellationToken)
        => FetchFinalizedInvoicesAsync(query: null, cancellationToken);

    Task<IReadOnlyList<AcumaticaInvoiceDto>> FetchFinalizedInvoicesAsync(
        AcumaticaInvoiceQuery? query,
        CancellationToken cancellationToken);

    Task<AcumaticaInvoiceDto?> FetchFinalizedInvoiceAsync(
        string invoiceId,
        CancellationToken cancellationToken);

    Task<AcumaticaInventoryItemDto?> FetchInventoryItemAsync(
        string inventoryId,
        CancellationToken cancellationToken)
        => Task.FromResult<AcumaticaInventoryItemDto?>(null);
}

public sealed record AcumaticaInvoiceQuery(
    DateTimeOffset ModifiedFromInclusive,
    DateTimeOffset ModifiedToExclusive);
