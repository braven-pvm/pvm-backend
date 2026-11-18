using Microsoft.EntityFrameworkCore;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Reporting.Statements;

// The settings of one merchandiser company as a document with names, for people. The import
// resolves each name to its Acumatica id. Refer to docs/design/merchandiser-statements.md, section 2.

/// <param name="ValidFrom">The first month, as 2026-10.</param>
/// <param name="Payment">commission or retainer.</param>
public sealed record MerchandiserSettingsDocument(
    string Name,
    string Title,
    string ValidFrom,
    string Payment,
    decimal? RatePercent,
    decimal? RetainerAmount,
    bool DisChemSheet,
    IReadOnlyList<MerchandiserAreaDocument> Areas);

public sealed record MerchandiserAreaDocument(
    string Name,
    decimal? CommissionRatePercent,
    IReadOnlyList<MerchandiserLineDocument> Lines,
    IReadOnlyList<string> MakroStores);

/// <param name="CustomerGroup">The customer class: its name or its id.</param>
/// <param name="Reps">Rep names or ids. Empty: all reps.</param>
/// <param name="Customers">Customer ids. Empty: all customers of the group and the reps.</param>
public sealed record MerchandiserLineDocument(
    string Label,
    string CustomerGroup,
    IReadOnlyList<string> Reps,
    IReadOnlyList<string> Customers,
    bool DisChem);

public sealed record MerchandiserSettingsImportResult(bool Saved, IReadOnlyList<string> Problems);

public sealed class MerchandiserSettingsService(PvmDbContext db, TimeProvider timeProvider)
{
    /// <summary>The settings of the company valid now, with names in place of ids where the names are known.</summary>
    public async Task<MerchandiserSettingsDocument?> ExportAsync(string companyName, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var month = new DateOnly(today.Year, today.Month, 1);
        var company = await db.MerchandiserCompanies.AsNoTracking()
            .Where(item => item.Name == companyName && item.ValidFrom <= month)
            .OrderByDescending(item => item.ValidFrom)
            .FirstOrDefaultAsync(cancellationToken)
            ?? await db.MerchandiserCompanies.AsNoTracking().Where(item => item.Name == companyName).OrderBy(item => item.ValidFrom).FirstOrDefaultAsync(cancellationToken);
        if (company is null)
        {
            return null;
        }

        var areas = await db.MerchandiserAreas.AsNoTracking().Where(area => area.CompanyId == company.Id).OrderBy(area => area.Order).ToListAsync(cancellationToken);
        var areaIds = areas.Select(area => area.Id).ToList();
        var lines = await db.MerchandiserLines.AsNoTracking().Where(line => areaIds.Contains(line.AreaId)).OrderBy(line => line.Order).ToListAsync(cancellationToken);
        var links = await db.MerchandiserMakroStores.AsNoTracking().Where(link => areaIds.Contains(link.AreaId)).ToListAsync(cancellationToken);
        var storeIds = links.Select(link => link.StoreId).ToList();
        var stores = await db.RetailStores.AsNoTracking().Where(store => storeIds.Contains(store.Id)).ToDictionaryAsync(store => store.Id, store => store.Name, cancellationToken);
        var reps = await db.ReportingSalesReps.AsNoTracking().ToDictionaryAsync(rep => rep.SalesRepId, rep => rep.Name, cancellationToken);
        var classes = await db.ReportingCustomers.AsNoTracking()
            .Where(customer => customer.CustomerClassId != null && customer.CustomerClassName != null)
            .Select(customer => new { customer.CustomerClassId, customer.CustomerClassName })
            .Distinct()
            .ToListAsync(cancellationToken);
        var className = classes.GroupBy(item => item.CustomerClassId!).ToDictionary(group => group.Key, group => group.First().CustomerClassName!);

        return new MerchandiserSettingsDocument(
            company.Name,
            company.StatementTitle,
            $"{company.ValidFrom:yyyy-MM}",
            company.PaymentKind,
            company.PaymentRatePercent,
            company.RetainerAmount,
            company.ShowDisChemYearOnYear,
            areas.Select(area => new MerchandiserAreaDocument(
                area.Name,
                area.CommissionRatePercent,
                lines.Where(line => line.AreaId == area.Id).Select(line => new MerchandiserLineDocument(
                    line.Label,
                    className.GetValueOrDefault(line.CustomerClassId, line.CustomerClassId),
                    line.SalesRepIds.Select(id => reps.GetValueOrDefault(id, id)).ToList(),
                    line.CustomerIds,
                    line.IsDisChemLine)).ToList(),
                links.Where(link => link.AreaId == area.Id).Select(link => stores.GetValueOrDefault(link.StoreId, "?")).Order(StringComparer.Ordinal).ToList())).ToList());
    }

    /// <summary>
    /// Saves the settings as the version of the company that starts in <see cref="MerchandiserSettingsDocument.ValidFrom"/>.
    /// A version with the same start is replaced. Nothing is saved if a name does not resolve.
    /// </summary>
    public async Task<MerchandiserSettingsImportResult> ImportAsync(MerchandiserSettingsDocument document, string updatedBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(document.Name))
        {
            problems.Add("The company has no name.");
        }

        if (!DateOnly.TryParseExact($"{document.ValidFrom}-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var validFrom))
        {
            problems.Add("validFrom must be a month such as 2026-10.");
        }

        var isRetainer = string.Equals(document.Payment, MerchandiserPaymentKinds.Retainer, StringComparison.OrdinalIgnoreCase);
        if (!isRetainer && !string.Equals(document.Payment, MerchandiserPaymentKinds.Commission, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("payment must be commission or retainer.");
        }
        else if (isRetainer && document.RetainerAmount is not > 0m)
        {
            problems.Add("A retainer needs retainerAmount.");
        }

        if (document.Areas.Count == 0)
        {
            problems.Add("The company has no areas.");
        }

        // Names and ids that a line may use, from the reporting tables that the loads keep.
        var reps = await db.ReportingSalesReps.AsNoTracking().Select(rep => new { rep.SalesRepId, rep.Name }).ToListAsync(cancellationToken);
        var classes = await db.ReportingCustomers.AsNoTracking()
            .Where(customer => customer.CustomerClassId != null)
            .Select(customer => new { customer.CustomerClassId, customer.CustomerClassName })
            .Distinct()
            .ToListAsync(cancellationToken);
        var makro = await db.RetailStores.AsNoTracking().Where(store => store.Retailer == "makro").Select(store => new { store.Id, store.StoreCode, store.Name }).ToListAsync(cancellationToken);
        var customerIds = document.Areas.SelectMany(area => area.Lines).SelectMany(line => line.Customers).Distinct().ToList();
        var knownCustomers = await db.ReportingCustomers.AsNoTracking().Where(customer => customerIds.Contains(customer.CustomerId)).Select(customer => customer.CustomerId).ToListAsync(cancellationToken);

        string? Rep(string value) => reps.FirstOrDefault(rep => Same(rep.SalesRepId, value) || Same(rep.Name, value))?.SalesRepId;
        string? Class(string value) => classes.FirstOrDefault(item => Same(item.CustomerClassId, value) || Same(item.CustomerClassName, value))?.CustomerClassId;
        Guid? Store(string value) => makro.FirstOrDefault(store => Same(store.StoreCode, value) || Same(store.Name, value))?.Id;

        var areas = new List<(MerchandiserAreaDocument Area, List<(MerchandiserLineDocument Line, string Class, List<string> Reps)> Lines, List<Guid> Stores)>();
        foreach (var area in document.Areas)
        {
            var lines = new List<(MerchandiserLineDocument, string, List<string>)>();
            foreach (var line in area.Lines)
            {
                var cls = Class(line.CustomerGroup);
                if (cls is null)
                {
                    problems.Add($"{area.Name} / {line.Label}: the customer group '{line.CustomerGroup}' is not known.");
                }

                var repIds = new List<string>();
                foreach (var rep in line.Reps)
                {
                    if (Rep(rep) is { } id)
                    {
                        repIds.Add(id);
                    }
                    else
                    {
                        problems.Add($"{area.Name} / {line.Label}: the rep '{rep}' is not known.");
                    }
                }

                problems.AddRange(line.Customers.Where(id => !knownCustomers.Contains(id, StringComparer.OrdinalIgnoreCase))
                    .Select(id => $"{area.Name} / {line.Label}: the customer '{id}' is not known."));
                lines.Add((line, cls ?? string.Empty, repIds));
            }

            var stores = new List<Guid>();
            foreach (var name in area.MakroStores)
            {
                if (Store(name) is { } id)
                {
                    stores.Add(id);
                }
                else
                {
                    problems.Add($"{area.Name}: the Makro store '{name}' is not known. Load a Makro Dispo file first.");
                }
            }

            areas.Add((area, lines, stores));
        }

        if (problems.Count > 0)
        {
            return new MerchandiserSettingsImportResult(false, problems);
        }

        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var old = await db.MerchandiserCompanies.Where(item => item.Name == document.Name.Trim() && item.ValidFrom == validFrom).ToListAsync(cancellationToken);
        db.MerchandiserCompanies.RemoveRange(old);
        await db.SaveChangesAsync(cancellationToken);

        var company = new MerchandiserCompanyEntity
        {
            Id = Guid.NewGuid(),
            Name = document.Name.Trim(),
            StatementTitle = string.IsNullOrWhiteSpace(document.Title) ? document.Name.Trim().ToUpperInvariant() : document.Title.Trim(),
            PaymentKind = isRetainer ? MerchandiserPaymentKinds.Retainer : MerchandiserPaymentKinds.Commission,
            PaymentRatePercent = isRetainer ? null : document.RatePercent,
            RetainerAmount = isRetainer ? document.RetainerAmount : null,
            ShowDisChemYearOnYear = document.DisChemSheet,
            ValidFrom = validFrom,
            UpdatedBy = updatedBy,
            UpdatedAt = now,
        };
        db.MerchandiserCompanies.Add(company);
        var areaOrder = 0;
        foreach (var (area, lines, stores) in areas)
        {
            var areaEntity = new MerchandiserAreaEntity { Id = Guid.NewGuid(), CompanyId = company.Id, Name = area.Name.Trim(), Order = ++areaOrder, CommissionRatePercent = area.CommissionRatePercent };
            db.MerchandiserAreas.Add(areaEntity);
            var lineOrder = 0;
            foreach (var (line, cls, repIds) in lines)
            {
                db.MerchandiserLines.Add(new MerchandiserLineEntity
                {
                    Id = Guid.NewGuid(),
                    AreaId = areaEntity.Id,
                    Label = line.Label.Trim(),
                    Order = ++lineOrder,
                    CustomerClassId = cls,
                    SalesRepIds = repIds.Distinct().ToList(),
                    CustomerIds = line.Customers.Distinct().ToList(),
                    IsDisChemLine = line.DisChem,
                });
            }

            foreach (var store in stores.Distinct())
            {
                db.MerchandiserMakroStores.Add(new MerchandiserMakroStoreEntity { AreaId = areaEntity.Id, StoreId = store });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new MerchandiserSettingsImportResult(true, []);
    }

    private static bool Same(string? a, string b) =>
        a is not null && string.Equals(string.Join(' ', a.Split(' ', StringSplitOptions.RemoveEmptyEntries)), string.Join(' ', b.Split(' ', StringSplitOptions.RemoveEmptyEntries)), StringComparison.OrdinalIgnoreCase);
}
