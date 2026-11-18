using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pvm.Application.Messaging;
using Pvm.Application.Reporting.SellOut;
using Pvm.Application.Submissions;
using Pvm.Domain.Reporting;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Reporting.SellOut;

/// <summary>The outcome of a preview or an import of one sell-out file.</summary>
/// <param name="ImportId">The import row. Empty for a preview.</param>
/// <param name="Reason">Why the import refused or failed the file.</param>
/// <param name="RowsLoaded">Month rows that the import writes.</param>
/// <param name="RowsUnmapped">Month rows with a product code that maps to no Acumatica item.</param>
/// <param name="RowsRefused">Rows of the file that the parser could not use.</param>
public sealed record SellOutImportResult(
    Guid ImportId,
    string Status,
    string? Source,
    string? Reason,
    IReadOnlyList<DateOnly> Months,
    int RowsRead,
    int RowsLoaded,
    int RowsUnmapped,
    int RowsRefused,
    int NewStores,
    IReadOnlyList<string> UnmappedProducts,
    IReadOnlyList<SellOutParseIssue> Issues);

/// <summary>
/// Loads one sell-out file: it finds the format, maps stores and products, and replaces the
/// months of that source in one transaction. A file loads only once. Refer to
/// docs/design/sell-out-import.md, section 4.
/// </summary>
public sealed class SellOutImportService(
    PvmDbContext db,
    IEnumerable<ISellOutParser> parsers,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<SellOutImportService> logger,
    IPayloadArchive? archive = null)
{
    public const string RunType = "sell-out-import";

    /// <summary>The share of bad rows above which the import refuses the whole file.</summary>
    public const decimal MaxBadRowShare = 0.05m;

    private readonly IReadOnlyList<ISellOutParser> _parsers = parsers.ToList();

    /// <summary>Reads and maps the file, and tells what an import would do. It writes nothing.</summary>
    public async Task<SellOutImportResult> PreviewAsync(SellOutFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        var hash = Hash(file.Content);
        var loaded = await LoadedImportAsync(hash, cancellationToken);
        if (loaded is not null)
        {
            return Refusal(Guid.Empty, null, $"already-loaded:{loaded.Value}");
        }

        var (parsed, refusal) = Read(file);
        if (parsed is null)
        {
            return Refusal(Guid.Empty, null, refusal!);
        }

        var plan = await PlanAsync(parsed, cancellationToken);
        return Outcome(Guid.Empty, SellOutImportStatuses.Previewed, parsed, plan, TooManyBadRows(parsed) ? "too-many-bad-rows" : null);
    }

    /// <summary>Loads the file. A refused file changes no fact, but its import row and issues stay for the log.</summary>
    public async Task<SellOutImportResult> ImportAsync(SellOutFile file, string receivedBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(receivedBy);
        var now = timeProvider.GetUtcNow();
        var import = new SellOutImportEntity
        {
            Id = Guid.NewGuid(),
            Source = "unknown",
            FileName = Truncate(Path.GetFileName(file.FileName), 260),
            FileSha256 = Hash(file.Content),
            FileSize = file.Content.LongLength,
            FileDate = file.ReceivedOn,
            ReceivedBy = Truncate(receivedBy, 320),
            Status = SellOutImportStatuses.Refused,
            CreatedAt = now,
        };

        var loaded = await LoadedImportAsync(import.FileSha256, cancellationToken);
        if (loaded is not null)
        {
            return await RefuseAsync(import, null, $"already-loaded:{loaded.Value}", cancellationToken);
        }

        var (parsed, refusal) = Read(file);
        if (parsed is null)
        {
            return await RefuseAsync(import, null, refusal!, cancellationToken);
        }

        import.Source = parsed.Source;
        import.FileDate = parsed.FileDate;
        import.FirstMonth = parsed.Months.Count > 0 ? parsed.Months[0] : null;
        import.LastMonth = parsed.Months.Count > 0 ? parsed.Months[^1] : null;
        import.RowsRead = parsed.RowsRead;
        import.RowsRefused = parsed.Issues.Count;
        if (TooManyBadRows(parsed))
        {
            return await RefuseAsync(import, parsed, "too-many-bad-rows", cancellationToken);
        }

        import.ArchivePath = await ArchiveAsync(import, file, cancellationToken);
        var run = NewRun(import, now);
        db.IntegrationRuns.Add(run);

        Plan plan;
        try
        {
            var strategy = db.Database.CreateExecutionStrategy();
            plan = await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                var result = await PlanAsync(parsed, cancellationToken);
                await WriteAsync(import, parsed, result, now, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Nothing of the file is in the facts. Keep the import and the run as failed, for the log.
            db.ChangeTracker.Clear();
            import.Status = SellOutImportStatuses.Failed;
            import.StatusReason = Truncate(exception.Message, 1000);
            db.SellOutImports.Add(import);
            run.Status = IntegrationRunStatuses.Failed;
            run.FailedCount = 1;
            run.ErrorCode = "sell-out-import-failed";
            run.ErrorSummary = Truncate(exception.Message, 1000);
            run.CompletedAt = timeProvider.GetUtcNow();
            run.UpdatedAt = run.CompletedAt.Value;
            db.IntegrationRuns.Add(run);
            await db.SaveChangesAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            logger.LogError(exception, "sell-out.import.failed ImportId={ImportId} Source={Source}", import.Id, parsed.Source);
            throw;
        }

        db.ChangeTracker.Clear();
        logger.LogInformation(
            "sell-out.import.loaded ImportId={ImportId} Source={Source} Rows={Rows} Loaded={Loaded} Unmapped={Unmapped} Refused={Refused} NewStores={NewStores}",
            import.Id, parsed.Source, parsed.RowsRead, parsed.MonthRows.Count, plan.UnmappedRows, parsed.Issues.Count, plan.NewStores.Count);
        return Outcome(import.Id, SellOutImportStatuses.Loaded, parsed, plan, null);
    }

    /// <summary>Maps a product code of a source to an Acumatica item, and fills the item on the rows that have the code.</summary>
    /// <returns>The month and stock rows that changed.</returns>
    public async Task<int> MapProductAsync(
        string source,
        string productCode,
        string inventoryId,
        decimal unitsPerEach,
        string mappedBy,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(productCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(inventoryId);
        if (unitsPerEach <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitsPerEach), "Units per each must be positive.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var map = await db.SellOutProductMaps.SingleOrDefaultAsync(
            item => item.Source == source && item.SourceProductCode == productCode, cancellationToken);
        if (map is null)
        {
            map = new SellOutProductMapEntity
            {
                Source = source,
                SourceProductCode = productCode,
                InventoryId = inventoryId,
                Method = SellOutMapMethods.Manual,
                MappedBy = mappedBy,
            };
            db.SellOutProductMaps.Add(map);
        }

        var oldFactor = map.UnitsPerEach;
        map.InventoryId = inventoryId;
        map.UnitsPerEach = unitsPerEach;
        map.Method = SellOutMapMethods.Manual;
        map.MappedBy = mappedBy;
        map.MappedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        // UnitsEach holds the units of the source times the old factor. Scale it to the new factor.
        var scale = unitsPerEach / oldFactor;
        var months = await db.SellOutMonths
            .Where(month => month.Source == source && month.SourceProductCode == productCode)
            .ExecuteUpdateAsync(set => set
                .SetProperty(month => month.InventoryId, inventoryId)
                .SetProperty(month => month.UnitsEach, month => month.UnitsEach * scale), cancellationToken);
        var stock = await db.StoreStockSnapshots
            .Where(snapshot => snapshot.Source == source && snapshot.SourceProductCode == productCode)
            .ExecuteUpdateAsync(set => set.SetProperty(snapshot => snapshot.InventoryId, inventoryId), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return months + stock;
    }

    private (SellOutParseResult? Parsed, string? Refusal) Read(SellOutFile file)
    {
        var matches = _parsers.Where(parser => parser.CanRead(file)).ToList();
        if (matches.Count == 0)
        {
            return (null, "unknown-format");
        }

        if (matches.Count > 1)
        {
            return (null, $"several-formats:{string.Join(',', matches.Select(parser => parser.Source))}");
        }

        try
        {
            return (matches[0].Parse(file), null);
        }
        catch (SellOutFormatException exception)
        {
            return (null, $"bad-format:{exception.Message}");
        }
    }

    private static bool TooManyBadRows(SellOutParseResult parsed) =>
        parsed.Issues.Count > 0 && parsed.Issues.Count > MaxBadRowShare * Math.Max(1, parsed.RowsRead);

    private async Task<Plan> PlanAsync(SellOutParseResult parsed, CancellationToken cancellationToken)
    {
        var codes = parsed.MonthRows.Select(row => row.StoreCode).Concat(parsed.StockRows.Select(row => row.StoreCode)).Distinct().ToList();
        var stores = await db.RetailStores
            .Where(store => store.Retailer == parsed.Retailer && codes.Contains(store.StoreCode))
            .ToDictionaryAsync(store => store.StoreCode, StringComparer.Ordinal, cancellationToken);
        var newStores = codes.Where(code => !stores.ContainsKey(code)).ToList();

        var products = parsed.MonthRows.Select(row => (row.ProductCode, row.Gtin))
            .Concat(parsed.StockRows.Select(row => (row.ProductCode, row.Gtin)))
            .GroupBy(product => product.ProductCode)
            .ToDictionary(group => group.Key, group => group.Select(product => product.Gtin).FirstOrDefault(gtin => gtin is not null));
        var codesToMap = products.Keys.ToList();
        var maps = await db.SellOutProductMaps
            .Where(map => map.Source == parsed.Source && codesToMap.Contains(map.SourceProductCode))
            .ToDictionaryAsync(map => map.SourceProductCode, StringComparer.Ordinal, cancellationToken);

        var newMaps = new List<SellOutProductMapEntity>();
        var unmapped = products.Keys.Where(code => !maps.ContainsKey(code)).ToList();
        if (unmapped.Count > 0)
        {
            var gtins = unmapped.Select(code => products[code]).OfType<string>().ToList();
            var byGtin = await db.ProductGtins.Where(item => gtins.Contains(item.Gtin))
                .ToDictionaryAsync(item => item.Gtin, item => item.InventoryId, StringComparer.Ordinal, cancellationToken);
            var byShopriteArticle = parsed.Source == ShopriteVasParser.SourceName
                ? await db.ShopriteItemMappings.Where(item => unmapped.Contains(item.ShopriteBuyerItemId))
                    .ToDictionaryAsync(item => item.ShopriteBuyerItemId, item => item.AcumaticaInventoryId, StringComparer.Ordinal, cancellationToken)
                : new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var code in unmapped)
            {
                var (inventoryId, method) = products[code] is { } gtin && byGtin.TryGetValue(gtin, out var fromGtin)
                    ? (fromGtin, SellOutMapMethods.Gtin)
                    : byShopriteArticle.TryGetValue(code, out var fromShoprite)
                        ? (fromShoprite, SellOutMapMethods.ShopriteItemMapping)
                        : ((string?)null, (string?)null);
                if (inventoryId is not null)
                {
                    newMaps.Add(new SellOutProductMapEntity
                    {
                        Source = parsed.Source,
                        SourceProductCode = code,
                        InventoryId = inventoryId,
                        Method = method!,
                        MappedBy = "system:sell-out-import",
                    });
                }
            }
        }

        foreach (var map in newMaps)
        {
            maps[map.SourceProductCode] = map;
        }

        var stillUnmapped = products.Keys.Where(code => !maps.ContainsKey(code)).Order(StringComparer.Ordinal).ToList();
        var unmappedRows = parsed.MonthRows.Count(row => !maps.ContainsKey(row.ProductCode));
        return new Plan(stores, newStores, maps, newMaps, stillUnmapped, unmappedRows);
    }

    private async Task WriteAsync(SellOutImportEntity import, SellOutParseResult parsed, Plan plan, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var code in plan.NewStores)
        {
            var name = parsed.MonthRows.Where(row => row.StoreCode == code).Select(row => row.StoreName)
                .Concat(parsed.StockRows.Where(row => row.StoreCode == code).Select(row => row.StoreName))
                .FirstOrDefault(storeName => !string.IsNullOrWhiteSpace(storeName)) ?? code;
            var store = new RetailStoreEntity
            {
                Id = Guid.NewGuid(),
                Retailer = parsed.Retailer,
                StoreCode = code,
                Name = Truncate(name, 256),
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.RetailStores.Add(store);
            plan.Stores[code] = store;
        }

        foreach (var map in plan.NewMaps)
        {
            map.MappedAt = now;
            db.SellOutProductMaps.Add(map);
        }

        // A later file of the same source and month replaces the earlier one, also for a store that is not in it.
        foreach (var month in parsed.Months)
        {
            await db.SellOutMonths
                .Where(row => row.Source == parsed.Source && row.Year == month.Year && row.Month == month.Month)
                .ExecuteDeleteAsync(cancellationToken);
        }

        await db.StoreStockSnapshots
            .Where(row => row.Source == parsed.Source && row.SnapshotDate == parsed.FileDate)
            .ExecuteDeleteAsync(cancellationToken);

        foreach (var row in parsed.MonthRows)
        {
            var map = plan.Maps.GetValueOrDefault(row.ProductCode);
            var financial = FinancialMonth.FromCalendar(row.Year, row.Month);
            db.SellOutMonths.Add(new SellOutMonthEntity
            {
                Id = Guid.NewGuid(),
                ImportId = import.Id,
                Source = parsed.Source,
                StoreId = plan.Stores[row.StoreCode].Id,
                InventoryId = map?.InventoryId,
                SourceProductCode = row.ProductCode,
                Gtin = Gtin(row.Gtin),
                Year = row.Year,
                Month = row.Month,
                FinancialYear = financial.FinancialYear,
                FinancialMonth = financial.Number,
                Units = row.UnitsEach,
                UnitsEach = row.UnitsEach * (map?.UnitsPerEach ?? 1m),
                ValueExclVat = row.ValueExclVat,
                IsPartial = row.IsPartial,
            });
        }

        foreach (var row in parsed.StockRows)
        {
            var map = plan.Maps.GetValueOrDefault(row.ProductCode);
            db.StoreStockSnapshots.Add(new StoreStockSnapshotEntity
            {
                Id = Guid.NewGuid(),
                ImportId = import.Id,
                Source = parsed.Source,
                StoreId = plan.Stores[row.StoreCode].Id,
                InventoryId = map?.InventoryId,
                SourceProductCode = row.ProductCode,
                Gtin = Gtin(row.Gtin),
                SnapshotDate = parsed.FileDate,
                StockOnHand = row.StockOnHand,
                StockOnOrder = row.StockOnOrder,
                StockInTransit = row.StockInTransit,
                StockValue = row.StockValue,
                WeeksOfStock = row.WeeksOfStock,
                DcStock = row.DcStock,
            });
        }

        AddIssues(import.Id, parsed.Issues);
        import.Status = SellOutImportStatuses.Loaded;
        import.RowsLoaded = parsed.MonthRows.Count;
        import.RowsUnmapped = plan.UnmappedRows;
        import.LoadedAt = now;
        db.SellOutImports.Add(import);

        var run = db.IntegrationRuns.Local.Single(item => item.ScheduleKey == $"{RunType}:{import.Id:N}");
        run.Status = IntegrationRunStatuses.Succeeded;
        run.ReceivedCount = parsed.RowsRead;
        run.CreatedCount = parsed.MonthRows.Count;
        run.SkippedCount = parsed.RowsIgnored;
        run.FailedCount = parsed.Issues.Count;
        run.CompletedAt = now;
        run.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<SellOutImportResult> RefuseAsync(
        SellOutImportEntity import,
        SellOutParseResult? parsed,
        string reason,
        CancellationToken cancellationToken)
    {
        import.Status = SellOutImportStatuses.Refused;
        import.StatusReason = Truncate(reason, 1000);
        db.SellOutImports.Add(import);
        if (parsed is not null)
        {
            AddIssues(import.Id, parsed.Issues);
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        logger.LogWarning("sell-out.import.refused ImportId={ImportId} Source={Source} Reason={Reason}", import.Id, import.Source, reason);
        return parsed is null
            ? Refusal(import.Id, null, reason)
            : Outcome(import.Id, SellOutImportStatuses.Refused, parsed, Plan.Empty, reason);
    }

    private void AddIssues(Guid importId, IEnumerable<SellOutParseIssue> issues)
    {
        foreach (var issue in issues)
        {
            db.SellOutImportIssues.Add(new SellOutImportIssueEntity
            {
                Id = Guid.NewGuid(),
                ImportId = importId,
                RowNumber = issue.RowNumber,
                Reason = Truncate(issue.Reason, 200),
                RawKey = issue.RawKey is null ? null : Truncate(issue.RawKey, 500),
            });
        }
    }

    private async Task<string?> ArchiveAsync(SellOutImportEntity import, SellOutFile file, CancellationToken cancellationToken)
    {
        if (archive is null)
        {
            return null;
        }

        // The archive keeps text, so the file goes in as Base64.
        var path = $"reporting/sell-out/{import.Source}/{import.CreatedAt:yyyy/MM}/{import.Id:N}.b64";
        var record = await archive.WriteAsync(
            new PayloadArchiveWrite(PayloadArchiveKind.SellOutFile, path, "application/octet-stream;base64", Convert.ToBase64String(file.Content)),
            cancellationToken);
        return Truncate(record.Location, 512);
    }

    private IntegrationRunEntity NewRun(SellOutImportEntity import, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        RunType = RunType,
        Trigger = import.ReceivedBy.StartsWith("mail:", StringComparison.Ordinal) ? "mail" : IntegrationRunTriggers.Manual,
        InitiatedBy = import.ReceivedBy,
        EnvironmentName = configuration["Pvm:EnvironmentName"] ?? "Development",
        CorrelationId = import.Id.ToString("N"),
        ScheduleKey = $"{RunType}:{import.Id:N}",
        Status = IntegrationRunStatuses.Running,
        AttemptCount = 1,
        CreatedAt = now,
        UpdatedAt = now,
        StartedAt = now,
    };

    private async Task<Guid?> LoadedImportAsync(string hash, CancellationToken cancellationToken) =>
        await db.SellOutImports
            .Where(import => import.FileSha256 == hash && import.Status == SellOutImportStatuses.Loaded)
            .Select(import => (Guid?)import.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static SellOutImportResult Outcome(Guid importId, string status, SellOutParseResult parsed, Plan plan, string? reason) => new(
        importId,
        status,
        parsed.Source,
        reason,
        parsed.Months,
        parsed.RowsRead,
        parsed.MonthRows.Count,
        plan.UnmappedRows,
        parsed.Issues.Count,
        plan.NewStores.Count,
        plan.UnmappedProducts,
        parsed.Issues);

    private static SellOutImportResult Refusal(Guid importId, string? source, string reason) =>
        new(importId, SellOutImportStatuses.Refused, source, reason, [], 0, 0, 0, 0, 0, [], []);

    // A GTIN has 8 to 14 digits. Any other value is not a GTIN and is not kept as one.
    private static string? Gtin(string? value) =>
        value is { Length: >= 8 and <= 14 } && value.All(char.IsAsciiDigit) ? value : null;

        private static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    private sealed record Plan(
        Dictionary<string, RetailStoreEntity> Stores,
        IReadOnlyList<string> NewStores,
        Dictionary<string, SellOutProductMapEntity> Maps,
        IReadOnlyList<SellOutProductMapEntity> NewMaps,
        IReadOnlyList<string> UnmappedProducts,
        int UnmappedRows)
    {
        public static Plan Empty { get; } = new(new(StringComparer.Ordinal), [], new(StringComparer.Ordinal), [], [], 0);
    }
}
