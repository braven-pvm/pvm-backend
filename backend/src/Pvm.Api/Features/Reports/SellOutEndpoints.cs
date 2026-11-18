using Pvm.Api.Auth;
using Pvm.Application.Reporting.SellOut;
using Pvm.Infrastructure.Reporting.SellOut;

namespace Pvm.Api.Features.Reports;

/// <summary>
/// Sell-out import endpoints. Refer to docs/design/sell-out-import.md, section 5.1.
/// <list type="bullet">
///   <item><c>POST /api/reports/sell-out/imports/preview</c>: read a file and tell what an import would do. Writes nothing.</item>
///   <item><c>POST /api/reports/sell-out/imports</c>: load a file.</item>
///   <item><c>GET /api/reports/sell-out/imports</c>: the latest imports.</item>
///   <item><c>GET /api/reports/sell-out/imports/{id}/issues</c>: the rows of an import that could not load.</item>
///   <item><c>GET /api/reports/sell-out/unmapped</c>: product codes that map to no Acumatica item.</item>
///   <item><c>GET /api/reports/sell-out/products</c>: Acumatica items to map to.</item>
///   <item><c>PUT /api/reports/sell-out/product-maps</c>: map a product code to an item.</item>
/// </list>
/// </summary>
public static class SellOutEndpoints
{
    /// <summary>A sell-out file is small. The largest real file is under 2 MB.</summary>
    internal const long MaxFileBytes = 25L * 1024 * 1024;

    public static IEndpointRouteBuilder MapSellOutEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports/sell-out");

        group.MapPost("/imports/preview", PreviewAsync)
            .RequireAuthorization("Invoices.Write")
            .DisableAntiforgery();

        group.MapPost("/imports", ImportAsync)
            .RequireAuthorization("Invoices.Write")
            .DisableAntiforgery();

        group.MapGet("/imports", ListImportsAsync).RequireAuthorization();
        group.MapGet("/imports/{id:guid}/issues", ListIssuesAsync).RequireAuthorization();
        group.MapGet("/unmapped", ListUnmappedAsync).RequireAuthorization();
        group.MapGet("/products", SearchProductsAsync).RequireAuthorization();

        group.MapPut("/product-maps", MapProductAsync)
            .RequireAuthorization("Invoices.Write");

        return app;
    }

    private static async Task<IResult> PreviewAsync(
        IFormFile file,
        SellOutImportService importer,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var (sellOutFile, problem) = await ReadFileAsync(file, timeProvider, cancellationToken);
        return sellOutFile is null ? problem! : Results.Ok(await importer.PreviewAsync(sellOutFile, cancellationToken));
    }

    private static async Task<IResult> ImportAsync(
        IFormFile file,
        SellOutImportService importer,
        CurrentAppUserAccessor currentUser,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var (sellOutFile, problem) = await ReadFileAsync(file, timeProvider, cancellationToken);
        return sellOutFile is null
            ? problem!
            : Results.Ok(await importer.ImportAsync(sellOutFile, ReceivedBy(currentUser), cancellationToken));
    }

    private static async Task<IResult> ListImportsAsync(SellOutQueries queries, int? take, CancellationToken cancellationToken) =>
        Results.Ok(await queries.ListImportsAsync(take ?? 50, cancellationToken));

    private static async Task<IResult> ListIssuesAsync(Guid id, SellOutQueries queries, CancellationToken cancellationToken) =>
        Results.Ok(await queries.ListIssuesAsync(id, cancellationToken));

    private static async Task<IResult> ListUnmappedAsync(SellOutQueries queries, CancellationToken cancellationToken) =>
        Results.Ok(await queries.ListUnmappedAsync(cancellationToken));

    private static async Task<IResult> SearchProductsAsync(SellOutQueries queries, string? search, CancellationToken cancellationToken) =>
        Results.Ok(await queries.SearchProductsAsync(search, 1000, cancellationToken));

    private static async Task<IResult> MapProductAsync(
        ProductMapRequest request,
        SellOutImportService importer,
        CurrentAppUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var problem = Validate(request);
        if (problem is not null)
        {
            return Results.BadRequest(new { message = problem });
        }

        var updated = await importer.MapProductAsync(
            request.Source.Trim(),
            request.ProductCode.Trim(),
            request.InventoryId.Trim(),
            request.UnitsPerEach ?? 1m,
            ReceivedBy(currentUser),
            cancellationToken);
        return Results.Ok(new { rowsUpdated = updated });
    }

    /// <summary>Reads the upload into memory. Returns a 400 result for an empty or a too large file.</summary>
    internal static async Task<(SellOutFile? File, IResult? Problem)> ReadFileAsync(
        IFormFile? file,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return (null, Results.BadRequest(new { message = "No file uploaded, or the file is empty." }));
        }

        if (file.Length > MaxFileBytes)
        {
            return (null, Results.BadRequest(new { file = file.FileName, message = "The file is larger than 25 MB." }));
        }

        using var memory = new MemoryStream((int)file.Length);
        await file.CopyToAsync(memory, cancellationToken);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().ToOffset(TimeSpan.FromHours(2)).DateTime);
        // Some browsers send the full path. Keep the name only, for a Windows path too.
        var name = file.FileName[(file.FileName.LastIndexOfAny(['/', '\\']) + 1)..];
        return (new SellOutFile(name, memory.ToArray(), today), null);
    }

    internal static string? Validate(ProductMapRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Source) || string.IsNullOrWhiteSpace(request.ProductCode))
        {
            return "Give the source and the product code.";
        }

        if (string.IsNullOrWhiteSpace(request.InventoryId))
        {
            return "Give the Acumatica item.";
        }

        if (request.UnitsPerEach is { } units && (units <= 0 || units > 10000))
        {
            return "Units per item must be more than 0 and at most 10000.";
        }

        return null;
    }

    internal static string ReceivedBy(CurrentAppUserAccessor currentUser) =>
        currentUser.User is { } user ? $"upload:{user.Email}" : "upload:unknown";
}

/// <param name="UnitsPerEach">Single items in one unit of the source. Empty means 1.</param>
public sealed record ProductMapRequest(string Source, string ProductCode, string InventoryId, decimal? UnitsPerEach);
