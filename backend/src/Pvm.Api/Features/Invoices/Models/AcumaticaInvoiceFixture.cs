using Pvm.Application.Acumatica;

namespace Pvm.Api.Features.Invoices.Models;

public sealed record AcumaticaInvoiceFixture(
    string SupplierGln,
    string StoreDcGln,
    AcumaticaInvoiceDto Invoice);
