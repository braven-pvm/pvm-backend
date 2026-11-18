using Microsoft.EntityFrameworkCore;
using Pvm.Application.Reporting.Statements;
using Pvm.Domain.Reporting;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Pvm.Infrastructure.Reporting.SellOut;

namespace Pvm.Infrastructure.Reporting.Statements;

public sealed record MerchandiserCompanySummary(Guid Id, string Name, DateOnly ValidFrom, DateOnly? ValidTo);

/// <summary>
/// Loads the settings, the sell-in and the Makro values for a statement, and builds it. A
/// statement is computed when a person opens it, so it always shows the data of the last load.
/// </summary>
public sealed class MerchandiserStatementService(PvmDbContext db)
{
    public async Task<IReadOnlyList<MerchandiserCompanySummary>> ListCompaniesAsync(CancellationToken cancellationToken) =>
        await db.MerchandiserCompanies.AsNoTracking()
            .OrderBy(company => company.Name).ThenByDescending(company => company.ValidFrom)
            .Select(company => new MerchandiserCompanySummary(company.Id, company.Name, company.ValidFrom, company.ValidTo))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Builds the statement of the company for the months from <paramref name="firstMonth"/> to
    /// <paramref name="lastMonth"/> (each the first day of a month). The settings are those valid
    /// in the last month. Returns null if the company has no settings for that month.
    /// </summary>
    public async Task<MerchandiserStatement?> BuildAsync(string companyName, DateOnly firstMonth, DateOnly lastMonth, CancellationToken cancellationToken)
    {
        firstMonth = new DateOnly(firstMonth.Year, firstMonth.Month, 1);
        lastMonth = new DateOnly(lastMonth.Year, lastMonth.Month, 1);
        var company = await db.MerchandiserCompanies.AsNoTracking()
            .Where(item => item.Name == companyName && item.ValidFrom <= lastMonth && (item.ValidTo == null || item.ValidTo >= lastMonth))
            .OrderByDescending(item => item.ValidFrom)
            .FirstOrDefaultAsync(cancellationToken);
        if (company is null)
        {
            return null;
        }

        var areas = await db.MerchandiserAreas.AsNoTracking().Where(area => area.CompanyId == company.Id).OrderBy(area => area.Order).ToListAsync(cancellationToken);
        var areaIds = areas.Select(area => area.Id).ToList();
        var lines = await db.MerchandiserLines.AsNoTracking().Where(line => areaIds.Contains(line.AreaId)).OrderBy(line => line.Order).ToListAsync(cancellationToken);
        var stores = await db.MerchandiserMakroStores.AsNoTracking().Where(link => areaIds.Contains(link.AreaId)).ToListAsync(cancellationToken);

        var settings = new StatementCompany(
            company.Name,
            company.StatementTitle,
            company.PaymentKind == MerchandiserPaymentKinds.Retainer,
            company.PaymentRatePercent,
            company.RetainerAmount,
            company.ShowDisChemYearOnYear,
            areas.Select(area => new StatementAreaSetting(
                area.Name,
                area.CommissionRatePercent,
                lines.Where(line => line.AreaId == area.Id)
                    .Select(line => new StatementLineSetting(line.Label, line.CustomerClassId, line.SalesRepIds, line.CustomerIds, line.IsDisChemLine))
                    .ToList(),
                stores.Where(link => link.AreaId == area.Id).Select(link => link.StoreId).ToList())).ToList());

        // The Dis-Chem sheet needs the last 3 months of this year and of the year before.
        var readFrom = company.ShowDisChemYearOnYear
            ? Min(firstMonth, lastMonth.AddMonths(1 - MerchandiserStatementBuilder.DisChemMonths).AddYears(-1))
            : firstMonth;
        var sellIn = await ReadSellInAsync(lines.Select(line => line.CustomerClassId).Distinct().ToList(), readFrom, lastMonth, cancellationToken);
        var makro = await ReadMakroAsync(stores.Select(link => link.StoreId).Distinct().ToList(), firstMonth, lastMonth, cancellationToken);
        return MerchandiserStatementBuilder.Build(settings, firstMonth, lastMonth, sellIn, makro);
    }

    private async Task<IReadOnlyList<StatementSellIn>> ReadSellInAsync(
        IReadOnlyList<string> classes, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var first = FinancialMonth.FromCalendar(from.Year, from.Month);
        var last = FinancialMonth.FromCalendar(to.Year, to.Month);
        var (firstKey, lastKey) = ((first.FinancialYear * 100) + first.Number, (last.FinancialYear * 100) + last.Number);
        var rows = await db.SellInReportLines.AsNoTracking()
            .Where(line => line.CustomerClassId != null && classes.Contains(line.CustomerClassId))
            .Where(line => (line.FinancialYear * 100) + line.FinancialMonth >= firstKey && (line.FinancialYear * 100) + line.FinancialMonth <= lastKey)
            .GroupBy(line => new { line.CustomerId, line.CustomerName, line.CustomerClassId, line.SalesRepId, line.FinancialYear, line.FinancialMonth })
            .Select(group => new { group.Key.CustomerId, group.Key.CustomerName, group.Key.CustomerClassId, group.Key.SalesRepId, group.Key.FinancialYear, group.Key.FinancialMonth, Amount = group.Sum(line => line.Amount) })
            .ToListAsync(cancellationToken);
        return rows.Select(row =>
        {
            var month = FinancialMonth.Create(row.FinancialYear, row.FinancialMonth).FirstDay;
            return new StatementSellIn(row.CustomerId, row.CustomerName ?? row.CustomerId, row.CustomerClassId, row.SalesRepId, new DateOnly(month.Year, month.Month, 1), row.Amount);
        }).ToList();
    }

    private async Task<IReadOnlyList<StatementMakroValue>> ReadMakroAsync(
        IReadOnlyList<Guid> storeIds, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var (fromKey, toKey) = ((from.Year * 100) + from.Month, (to.Year * 100) + to.Month);
        var rows = await db.SellOutMonths.AsNoTracking()
            .Where(month => month.Source == MakroDispoParser.SourceName && storeIds.Contains(month.StoreId))
            .Where(month => (month.Year * 100) + month.Month >= fromKey && (month.Year * 100) + month.Month <= toKey)
            .GroupBy(month => new { month.StoreId, month.Year, month.Month })
            .Select(group => new { group.Key.StoreId, group.Key.Year, group.Key.Month, Value = group.Sum(month => month.ValueExclVat ?? 0m), Partial = group.Any(month => month.IsPartial) })
            .ToListAsync(cancellationToken);
        var names = await db.RetailStores.AsNoTracking().Where(store => storeIds.Contains(store.Id)).ToDictionaryAsync(store => store.Id, store => store.Name, cancellationToken);
        return rows.Select(row => new StatementMakroValue(row.StoreId, names.GetValueOrDefault(row.StoreId, "?"), new DateOnly(row.Year, row.Month, 1), row.Value, row.Partial)).ToList();
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
