using Microsoft.EntityFrameworkCore;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Documents;

/// <summary>
/// The documents search by invoice number (design 6.9). A document is found by the invoice it was
/// filed to, and also, while it waits on the review list, by an invoice number read from the paper
/// or by a candidate invoice the matcher offered. All comparisons ignore case.
/// </summary>
public static class DocumentSearch
{
    /// <summary>
    /// The documents for one invoice number, as a query the caller can filter, sort and page further.
    /// On Postgres the extraction (jsonb) is searched element by element: the
    /// <c>InvoiceNumbers</c> array Claude read and each candidate's <c>referenceNumber</c>, so a
    /// number that only appears in a note or a name does not count. Other providers (the EF InMemory
    /// database of the API tests) cannot run that SQL and search the stored JSON text for the
    /// number in quotes instead.
    /// </summary>
    public static IQueryable<CapturedDocumentEntity> ByInvoice(PvmDbContext dbContext, string invoice)
    {
        var reference = invoice.Trim().ToUpperInvariant();

        if (dbContext.Database.IsNpgsql())
        {
            return dbContext.CapturedDocuments.FromSqlInterpolated($"""
                SELECT d.*
                FROM captured_documents AS d
                WHERE upper(d."MatchedReference") = {reference}
                   OR EXISTS (
                        SELECT 1
                        FROM jsonb_array_elements_text(
                            CASE WHEN jsonb_typeof(d."ExtractionJson" -> 'InvoiceNumbers') = 'array'
                                 THEN d."ExtractionJson" -> 'InvoiceNumbers'
                                 ELSE '[]'::jsonb END) AS number(value)
                        WHERE upper(btrim(number.value)) = {reference})
                   OR EXISTS (
                        SELECT 1
                        FROM jsonb_array_elements(
                            CASE WHEN jsonb_typeof(d."ExtractionJson" -> 'candidates') = 'array'
                                 THEN d."ExtractionJson" -> 'candidates'
                                 ELSE '[]'::jsonb END) AS candidate(value)
                        WHERE upper(candidate.value ->> 'referenceNumber') = {reference})
                """);
        }

        var quoted = $"\"{reference}\"";
        return dbContext.CapturedDocuments.Where(item =>
            (item.MatchedReference != null && item.MatchedReference.ToUpper() == reference)
            || (item.ExtractionJson != null && item.ExtractionJson.ToUpper().Contains(quoted)));
    }
}
