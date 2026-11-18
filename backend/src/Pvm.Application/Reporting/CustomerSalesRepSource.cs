namespace Pvm.Application.Reporting;

/// <summary>
/// The current rep of one customer: the default salesperson on the customer in Acumatica.
/// The sales workbook shows all sales of a customer under this rep, also the sales of earlier
/// years, so a report "by rep" moves with the customer when the rep changes.
/// </summary>
public sealed record CustomerSalesRep(string CustomerId, string? SalesRepId, string? SalesRepName);

/// <summary>Reads the current rep of every customer in Acumatica.</summary>
public interface ICustomerSalesRepSource
{
    Task<IReadOnlyList<CustomerSalesRep>> ReadAllAsync(CancellationToken cancellationToken);
}
