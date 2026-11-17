namespace Pvm.Application.Documents;

/// <summary>
/// The Acumatica side of document capture (design 6.5 steps 1 and 2, and 6.6): find the invoices an
/// extraction points to, search invoices for the review list, and attach a filed PDF. Only
/// documents of the type "Invoice" are candidates, for all customers.
/// </summary>
public interface IAcumaticaDocumentClient
{
    /// <summary>
    /// Looks up the invoices that the extracted invoice, sales-order and customer-order numbers
    /// point to. Returns one hit for each invoice and kind of number that found it; the status is
    /// returned as Acumatica gives it, so the matcher can leave voided and cancelled ones out.
    /// </summary>
    Task<IReadOnlyList<CandidateHit>> FindInvoicesAsync(
        PodExtraction extraction,
        CancellationToken cancellationToken);

    /// <summary>
    /// Invoices whose reference number starts with the query or whose customer name contains it;
    /// up to 20, newest first.
    /// </summary>
    Task<IReadOnlyList<InvoiceCandidate>> SearchInvoicesAsync(
        string query,
        CancellationToken cancellationToken);

    /// <summary>
    /// Attaches a file to an Acumatica record through its REST files endpoint. Supported entity:
    /// <c>SalesInvoice</c> (an invoice of the type "Invoice").
    /// </summary>
    Task AttachFileAsync(
        string entity,
        string referenceNumber,
        string fileName,
        Stream pdf,
        CancellationToken cancellationToken);
}
