using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pvm.Application.Messaging;
using Pvm.Application.Reporting;
using Pvm.Application.Submissions;
using Pvm.Domain.Reporting;
using Pvm.Infrastructure.Messaging;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Reporting;

/// <summary>The outcome of one sell-in load.</summary>
public sealed record SellInSyncResult(
    Guid RunId,
    PostingPeriod FromPeriod,
    PostingPeriod ToPeriod,
    int PeriodsRead,
    int RowsRead,
    int LinesWritten,
    int RowsSkipped);

/// <summary>
/// Loads Acumatica sell-in into the reporting schema, one posting period at a time. Each period
/// replaces the lines held for it, in one transaction, so a line deleted or moved in Acumatica
/// does not stay behind. The run stops at the first period that fails, records the failure, and
/// rethrows, so that the scheduler shows a failed run.
/// </summary>
public sealed class SellInSyncService(
    PvmDbContext db,
    ISellInSource source,
    IOptions<SellInSyncOptions> options,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<SellInSyncService> logger,
    IPayloadArchive? archive = null)
{
    public const string RunType = "acumatica-sell-in-sync";

    private readonly SellInSyncOptions _options = options.Value;

    /// <summary>The periods a run reads on this date: some back, the current one, some ahead, never before the floor.</summary>
    public static (PostingPeriod From, PostingPeriod To) ResolveWindow(DateOnly today, SellInSyncOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var current = PostingPeriod.Containing(today);
        var from = current.AddPeriods(-Math.Max(0, options.PeriodsBack));
        var to = current.AddPeriods(Math.Max(0, options.PeriodsAhead));
        if (!string.IsNullOrWhiteSpace(options.FirstPeriod))
        {
            var floor = PostingPeriod.Parse(options.FirstPeriod);
            if (from.CompareTo(floor) < 0)
            {
                from = floor;
            }
        }

        return (from, to);
    }

    public async Task<SellInSyncResult> RunAsync(
        PostingPeriod from,
        PostingPeriod to,
        string trigger,
        string initiatedBy,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            throw new InvalidOperationException("The sell-in load is switched off (SellIn:Enabled is false).");
        }

        if (from.CompareTo(to) > 0)
        {
            throw new ArgumentException($"The first period {from} is after the last period {to}.", nameof(from));
        }

        var run = await StartRunAsync(from, to, trigger, initiatedBy, cancellationToken);
        var periods = 0;
        var rowsRead = 0;
        var written = 0;
        var skipped = 0;
        var period = from;
        try
        {
            while (period.CompareTo(to) <= 0)
            {
                var read = await source.ReadPeriodAsync(period, cancellationToken);
                await ArchiveAsync(run.Id, read, cancellationToken);
                var count = await ReplacePeriodAsync(read, cancellationToken);

                periods++;
                rowsRead += read.Rows.Count + read.SkippedRows;
                written += count;
                skipped += read.SkippedRows;
                logger.LogInformation(
                    "sell-in.period.loaded RunId={RunId} Period={Period} Rows={Rows} Written={Written} Skipped={Skipped}",
                    run.Id,
                    period.DisplayId,
                    read.Rows.Count + read.SkippedRows,
                    count,
                    read.SkippedRows);
                period = period.AddPeriods(1);
            }
        }
        catch (Exception exception)
        {
            await CompleteRunAsync(run, IntegrationRunStatuses.Failed, rowsRead, written, skipped,
                exception is SellInReadException ? "sell-in-read-failed" : "sell-in-load-failed",
                $"Period {period.DisplayId}: {exception.Message}",
                CancellationToken.None);
            throw;
        }

        await CompleteRunAsync(run, IntegrationRunStatuses.Succeeded, rowsRead, written, skipped, null, null, cancellationToken);
        return new SellInSyncResult(run.Id, from, to, periods, rowsRead, written, skipped);
    }

    private async Task ArchiveAsync(Guid runId, SellInPeriodRead read, CancellationToken cancellationToken)
    {
        if (!_options.ArchiveRawResponses || archive is null)
        {
            return;
        }

        await archive.WriteAsync(
            new PayloadArchiveWrite(
                PayloadArchiveKind.AcumaticaSource,
                $"reporting/sell-in/{read.Period.InternalId}/{runId:N}.json",
                "application/json",
                read.RawJson),
            cancellationToken);
    }

    private async Task<int> ReplacePeriodAsync(SellInPeriodRead read, CancellationToken cancellationToken)
    {
        var duplicate = read.Rows
            .GroupBy(row => (row.DocumentType, row.ReferenceNumber, row.LineNumber))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            // A join in the inquiry returns a line twice. Loading it would double the sales.
            throw new SellInReadException(
                $"The inquiry returned document line {duplicate.Key.DocumentType.ToAcumaticaCode()} "
                + $"{duplicate.Key.ReferenceNumber} line {duplicate.Key.LineNumber} {duplicate.Count()} times. "
                + "Check the relations of the inquiry.");
        }

        var now = timeProvider.GetUtcNow();
        var postPeriod = read.Period.InternalId;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.SellInLines.Where(line => line.PostPeriod == postPeriod).ExecuteDeleteAsync(cancellationToken);
        db.SellInLines.AddRange(read.Rows.Select(row => ToLine(row, postPeriod, now)));
        await UpsertDimensionsAsync(read.Rows, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return read.Rows.Count;
    }

    private static SellInLineEntity ToLine(SellInRow row, string postPeriod, DateTimeOffset now)
    {
        var month = FinancialMonth.FromDate(row.DocumentDate);
        return new SellInLineEntity
        {
            Id = Guid.NewGuid(),
            DocumentType = row.DocumentType,
            ReferenceNumber = row.ReferenceNumber,
            LineNumber = row.LineNumber,
            DocumentDate = row.DocumentDate,
            FinancialYear = month.FinancialYear,
            FinancialMonth = month.Number,
            PostPeriod = postPeriod,
            CustomerId = row.CustomerId,
            SalesRepId = row.SalesRepId,
            InventoryId = row.InventoryId,
            WarehouseId = row.WarehouseId,
            LocationId = row.LocationId,
            Quantity = row.DocumentType.Signed(row.Quantity),
            Uom = row.Uom,
            BaseQuantity = row.DocumentType.Signed(row.BaseQuantity),
            Amount = row.DocumentType.Signed(row.Amount),
            // Npgsql stores timestamptz values in UTC only.
            AcumaticaLastModifiedAt = row.LastModifiedAt?.ToUniversalTime(),
            LoadedAt = now
        };
    }

    private async Task UpsertDimensionsAsync(IReadOnlyList<SellInRow> rows, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // The newest document of each key gives its current name and class.
        static IEnumerable<T> Newest<TKey, T>(IEnumerable<SellInRow> source, Func<SellInRow, TKey> key, Func<SellInRow, T> map)
            where TKey : notnull =>
            source.GroupBy(key).Select(group => map(group.MaxBy(row => row.DocumentDate)!));

        var customers = Newest(rows, row => row.CustomerId, row => row).ToList();
        var customerIds = customers.Select(row => row.CustomerId).ToList();
        var existingCustomers = await db.ReportingCustomers
            .Where(customer => customerIds.Contains(customer.CustomerId))
            .ToDictionaryAsync(customer => customer.CustomerId, cancellationToken);
        foreach (var row in customers)
        {
            if (!existingCustomers.TryGetValue(row.CustomerId, out var customer))
            {
                customer = new ReportingCustomerEntity { CustomerId = row.CustomerId, Name = row.CustomerName };
                db.ReportingCustomers.Add(customer);
            }

            customer.Name = row.CustomerName;
            customer.CustomerClassId = row.CustomerClassId;
            customer.CustomerClassName = row.CustomerClassName;
            customer.Province = row.Province;
            customer.UpdatedAt = now;
        }

        var products = Newest(rows, row => row.InventoryId, row => row).ToList();
        var inventoryIds = products.Select(row => row.InventoryId).ToList();
        var existingProducts = await db.ReportingProducts
            .Where(product => inventoryIds.Contains(product.InventoryId))
            .ToDictionaryAsync(product => product.InventoryId, cancellationToken);
        foreach (var row in products)
        {
            if (!existingProducts.TryGetValue(row.InventoryId, out var product))
            {
                product = new ReportingProductEntity { InventoryId = row.InventoryId, Description = row.ItemDescription };
                db.ReportingProducts.Add(product);
            }

            product.Description = row.ItemDescription;
            product.ItemClassId = row.ItemClassId;
            product.ItemClassName = row.ItemClassName;
            product.UpdatedAt = now;
        }

        var reps = Newest(rows.Where(row => row.SalesRepId is not null), row => row.SalesRepId!, row => row).ToList();
        var repIds = reps.Select(row => row.SalesRepId!).ToList();
        var existingReps = await db.ReportingSalesReps
            .Where(rep => repIds.Contains(rep.SalesRepId))
            .ToDictionaryAsync(rep => rep.SalesRepId, cancellationToken);
        foreach (var row in reps)
        {
            if (!existingReps.TryGetValue(row.SalesRepId!, out var rep))
            {
                rep = new ReportingSalesRepEntity { SalesRepId = row.SalesRepId!, Name = row.SalesRepName ?? row.SalesRepId! };
                db.ReportingSalesReps.Add(rep);
            }

            rep.Name = row.SalesRepName ?? row.SalesRepId!;
            rep.UpdatedAt = now;
        }

        var locations = Newest(
                rows.Where(row => row.WarehouseId is not null && row.LocationId is not null),
                row => (row.WarehouseId!, row.LocationId!),
                row => row)
            .ToList();
        var warehouseIds = locations.Select(row => row.WarehouseId!).Distinct().ToList();
        var existingLocations = (await db.ReportingWarehouseLocations
                .Where(location => warehouseIds.Contains(location.WarehouseId))
                .ToListAsync(cancellationToken))
            .ToDictionary(location => (location.WarehouseId, location.LocationId));
        foreach (var row in locations)
        {
            if (!existingLocations.TryGetValue((row.WarehouseId!, row.LocationId!), out var location))
            {
                location = new ReportingWarehouseLocationEntity { WarehouseId = row.WarehouseId!, LocationId = row.LocationId! };
                db.ReportingWarehouseLocations.Add(location);
            }

            location.WarehouseName = row.WarehouseName;
            location.LocationName = row.LocationName;
            location.UpdatedAt = now;
        }
    }

    private async Task<IntegrationRunEntity> StartRunAsync(
        PostingPeriod from,
        PostingPeriod to,
        string trigger,
        string initiatedBy,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var run = new IntegrationRunEntity
        {
            Id = Guid.NewGuid(),
            RunType = RunType,
            Trigger = trigger,
            InitiatedBy = initiatedBy,
            EnvironmentName = configuration["Pvm:EnvironmentName"] ?? "Development",
            CorrelationId = Guid.NewGuid().ToString("N"),
            ScheduleKey = $"{RunType}:{from.InternalId}-{to.InternalId}",
            Status = IntegrationRunStatuses.Running,
            AttemptCount = 1,
            QueryFrom = new DateTimeOffset(FinancialMonth.Create(from.FinancialYear, from.Number).FirstDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            QueryTo = new DateTimeOffset(FinancialMonth.Create(to.FinancialYear, to.Number).AddMonths(1).FirstDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            CreatedAt = now,
            UpdatedAt = now,
            StartedAt = now
        };
        db.IntegrationRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        return run;
    }

    private async Task CompleteRunAsync(
        IntegrationRunEntity run,
        string status,
        int rowsRead,
        int written,
        int skipped,
        string? errorCode,
        string? errorSummary,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var stored = await db.IntegrationRuns.SingleAsync(item => item.Id == run.Id, cancellationToken);
        var now = timeProvider.GetUtcNow();
        stored.Status = status;
        stored.ReceivedCount = rowsRead;
        stored.CreatedCount = written;
        stored.SkippedCount = skipped;
        stored.FailedCount = status == IntegrationRunStatuses.Failed ? 1 : 0;
        stored.ErrorCode = errorCode;
        stored.ErrorSummary = errorSummary is null ? null : OutboxRepository.SafeSummary(errorSummary);
        stored.CompletedAt = now;
        stored.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
    }
}
