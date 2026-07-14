namespace Pvm.Application.Acumatica;

public interface IAcumaticaInvoiceClient
{
    Task<IReadOnlyList<AcumaticaInvoiceDto>> FetchFinalizedInvoicesAsync(
        CancellationToken cancellationToken);
}
