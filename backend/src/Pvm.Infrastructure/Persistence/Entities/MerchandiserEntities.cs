namespace Pvm.Infrastructure.Persistence.Entities;

// The settings of the merchandiser statements. Refer to docs/design/merchandiser-statements.md, section 2.

public static class MerchandiserPaymentKinds
{
    /// <summary>PVM pays a percentage of the sales.</summary>
    public const string Commission = "commission";

    /// <summary>PVM pays a fixed amount each month.</summary>
    public const string Retainer = "retainer";
}

/// <summary>A merchandiser company that PVM pays for the sales in its area.</summary>
public sealed class MerchandiserCompanyEntity
{
    public Guid Id { get; set; }
    public required string Name { get; set; }

    /// <summary>The title on the statement, such as "CMR MERIDIAN".</summary>
    public required string StatementTitle { get; set; }

    public required string PaymentKind { get; set; }

    /// <summary>The commission on the grand total, in percent. Empty for a retainer.</summary>
    public decimal? PaymentRatePercent { get; set; }

    /// <summary>The fixed amount each month, in rand. Empty for a commission.</summary>
    public decimal? RetainerAmount { get; set; }

    /// <summary>Add the Dis-Chem sheet: sell-in for each store, this year and the year before.</summary>
    public bool ShowDisChemYearOnYear { get; set; }

    /// <summary>The first month that these settings apply to (day 1).</summary>
    public DateOnly ValidFrom { get; set; }

    /// <summary>The last month that these settings apply to (day 1). Empty: no end.</summary>
    public DateOnly? ValidTo { get; set; }

    public required string UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>An area of a company, with its own total and, optionally, its own commission.</summary>
public sealed class MerchandiserAreaEntity
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public required string Name { get; set; }
    public int Order { get; set; }

    /// <summary>The commission on the area total, in percent. Empty: no line for it.</summary>
    public decimal? CommissionRatePercent { get; set; }
}

/// <summary>
/// A line of an area: the sell-in of one customer class for a list of reps. A list of customers
/// limits the line further, such as the 2 Checkers distribution centres.
/// </summary>
public sealed class MerchandiserLineEntity
{
    public Guid Id { get; set; }
    public Guid AreaId { get; set; }
    public required string Label { get; set; }
    public int Order { get; set; }
    public required string CustomerClassId { get; set; }

    /// <summary>The Acumatica salesperson ids. Empty: all reps.</summary>
    public List<string> SalesRepIds { get; set; } = [];

    /// <summary>The Acumatica customer ids. Empty: all customers of the class and the reps.</summary>
    public List<string> CustomerIds { get; set; } = [];

    /// <summary>The line is the source of the Dis-Chem sheet.</summary>
    public bool IsDisChemLine { get; set; }
}

/// <summary>A Makro store whose Rand value counts in the Makro line of an area.</summary>
public sealed class MerchandiserMakroStoreEntity
{
    public Guid AreaId { get; set; }
    public Guid StoreId { get; set; }
}
