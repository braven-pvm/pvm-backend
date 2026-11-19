using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pvm.Api.Auth;
using Pvm.Application.Documents;
using Pvm.Infrastructure.Documents;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Api.Features.Documents;

/// <summary>
/// The workbench's Documents page and review list (design 6.8, 6.9):
/// <list type="bullet">
///   <item><c>GET /api/documents</c> — search, newest first, pages of 50. Without <c>type</c> it
///   searches every type (design 6.13); <c>type=POD</c> or <c>type=GENERAL</c> narrows it.</item>
///   <item><c>GET /api/documents/review</c> — the open review list, oldest first: NeedsReview and
///   Failed documents that are not rejected, and Received or Processing ones that are stuck. General
///   documents are never on it.</item>
///   <item><c>GET /api/documents/{id}</c>, <c>/{id}/file</c> and <c>/{id}/thumbnail</c> — one
///   document, its PDF, and the phone's JPEG of the first page (404 when there is none).</item>
///   <item><c>GET /api/documents/general</c> and <c>/general/filters</c> — the General scans list
///   (design 6.12), in <see cref="GeneralDocumentEndpoints"/>.</item>
///   <item><c>GET /api/documents/review/invoice-search</c>, <c>POST /{id}/file-to</c> and
///   <c>POST /{id}/reject</c> — the review actions.</item>
///   <item><c>POST /{id}/return-to-review</c> — takes a wrongly filed document, not yet attached in
///   Acumatica, back to the review list.</item>
///   <item><c>POST /{id}/process-again</c> — sends a Failed or stuck document through processing again.</item>
///   <item><c>POST /{id}/delete</c> — removes an unfiled document and its PDF for good (design 6.11).</item>
///   <item><c>POST /{id}/change-invoice</c> — links a filed POD, not yet attached in Acumatica, to
///   another invoice (design 6.13). Nothing is written to Acumatica.</item>
/// </list>
/// </summary>
public static class DocumentReviewEndpoints
{
    public const int PageSize = 50;

    private const int MaxReferenceLength = 64;

    public const int MaxChangeReasonLength = 500;

    public static IEndpointRouteBuilder MapDocumentReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/documents");

        group.MapGet("", SearchAsync).RequireAuthorization("Invoices.Read");
        group.MapGet("/review", ReviewListAsync).RequireAuthorization("Invoices.Read");
        group.MapGet("/review/invoice-search", InvoiceSearchAsync).RequireAuthorization("Invoices.Write");
        group.MapGet("/{id:guid}", DetailAsync).RequireAuthorization("Invoices.Read");
        group.MapGet("/{id:guid}/file", FileAsync).RequireAuthorization("Invoices.Read");
        group.MapGet("/{id:guid}/thumbnail", ThumbnailAsync).RequireAuthorization("Invoices.Read");
        GeneralDocumentEndpoints.Map(group);
        group.MapPost("/{id:guid}/file-to", FileToAsync).RequireAuthorization("Invoices.Write");
        group.MapPost("/{id:guid}/reject", RejectAsync).RequireAuthorization("Invoices.Write");
        group.MapPost("/{id:guid}/return-to-review", ReturnToReviewAsync).RequireAuthorization("Invoices.Write");
        group.MapPost("/{id:guid}/process-again", ProcessAgainAsync).RequireAuthorization("Invoices.Write");
        group.MapPost("/{id:guid}/delete", DeleteAsync).RequireAuthorization("Invoices.Write");
        group.MapPost("/{id:guid}/change-invoice", ChangeInvoiceAsync).RequireAuthorization("Invoices.Write");

        return app;
    }

    /// <summary>The signed-in user's email, or null when the user could not be resolved.</summary>
    internal static string? CurrentEmail(CurrentAppUserAccessor currentUser)
        => string.IsNullOrWhiteSpace(currentUser.User?.Email) ? null : currentUser.User.Email;

    /// <summary>
    /// A request under an authorised policy without a resolved user: the user middleware failed.
    /// Nothing is written under a made-up name.
    /// </summary>
    internal static IResult UserNotResolved()
        => Results.Problem("The signed-in user could not be resolved.", statusCode: StatusCodes.Status500InternalServerError);

    private static async Task<IResult> SearchAsync(
        PvmDbContext dbContext,
        IOptions<DocumentCaptureOptions> captureOptions,
        string? invoice,
        string? client,
        string? type,
        string? state,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? page,
        CancellationToken cancellationToken)
    {
        // An invoice number finds a document in any state: filed to it, read from its paper, or
        // among its candidates. A paper with two invoice numbers is found by both.
        var query = (string.IsNullOrWhiteSpace(invoice)
                ? dbContext.CapturedDocuments
                : DocumentSearch.ByInvoice(dbContext, invoice))
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(client))
        {
            var name = client.Trim().ToLowerInvariant();
            query = query.Where(item => item.MatchedCustomerName != null && item.MatchedCustomerName.ToLower().Contains(name));
        }

        // The Documents page lists every type (design 6.13); a type narrows it to POD or general.
        if (!string.IsNullOrWhiteSpace(type))
        {
            var code = type.Trim().ToUpperInvariant();
            query = query.Where(item => item.DocumentType.ToUpper() == code);
        }

        if (!string.IsNullOrWhiteSpace(state))
        {
            if (!Enum.TryParse<DocumentState>(state.Trim(), ignoreCase: true, out var parsed)
                || !Enum.IsDefined(parsed))
            {
                return BadRequest($"The state '{state}' is not known.");
            }

            query = query.Where(item => item.State == parsed);
        }

        if (from is not null)
        {
            var fromUtc = from.Value.ToUniversalTime();
            query = query.Where(item => item.UploadedAt >= fromUtc);
        }

        if (to is not null)
        {
            var toUtc = to.Value.ToUniversalTime();
            query = query.Where(item => item.UploadedAt < toUtc);
        }

        var pageNumber = Math.Max(page ?? 1, 1);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(item => item.UploadedAt)
            .ThenByDescending(item => item.Id)
            .Skip((pageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync(cancellationToken);

        var deviceNames = await DeviceNamesAsync(dbContext, items, cancellationToken);
        return Results.Ok(new
        {
            items = items.Select(item => SearchItem(item, deviceNames)),
            page = pageNumber,
            pageSize = PageSize,
            total,
            // So the list shows an Acumatica attach state only when attachments are written (design 6.13).
            acumaticaAttachMode = captureOptions.Value.AcumaticaAttachMode.ToString()
        });
    }

    private static async Task<IResult> ReviewListAsync(
        PvmDbContext dbContext,
        int? page,
        CancellationToken cancellationToken)
    {
        var stuckBefore = DateTimeOffset.UtcNow - DocumentReviewService.StuckAfter;
        var query = dbContext.CapturedDocuments
            .AsNoTracking()
            .Where(item => item.DocumentType != DocumentTypeCodes.General)
            .Where(item =>
                ((item.State == DocumentState.NeedsReview || item.State == DocumentState.Failed) && item.RejectedAt == null)
                || ((item.State == DocumentState.Received || item.State == DocumentState.Processing) && item.UploadedAt < stuckBefore));

        var pageNumber = Math.Max(page ?? 1, 1);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(item => item.UploadedAt)
            .ThenBy(item => item.Id)
            .Skip((pageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync(cancellationToken);

        var deviceNames = await DeviceNamesAsync(dbContext, items, cancellationToken);
        return Results.Ok(new
        {
            items = items.Select(item => new
            {
                id = item.Id,
                documentType = item.DocumentType,
                state = item.State.ToString(),
                reviewReason = item.ReviewReason?.ToString(),
                stuck = IsStuck(item, stuckBefore),
                pageCount = item.PageCount,
                uploadedBy = item.UploadedBy,
                uploadedAt = item.UploadedAt,
                scannerDeviceName = DeviceName(item, deviceNames),
                scannerStaffName = item.ScannerStaffName,
                updatedAt = item.UpdatedAt
            }),
            page = pageNumber,
            pageSize = PageSize,
            total
        });
    }

    private static async Task<IResult> DetailAsync(
        Guid id,
        PvmDbContext dbContext,
        IOptions<DocumentCaptureOptions> captureOptions,
        CancellationToken cancellationToken)
    {
        var document = await dbContext.CapturedDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        var (extraction, unreadableReason, candidates) = ReadExtraction(document.ExtractionJson);
        var deviceNames = await DeviceNamesAsync(dbContext, [document], cancellationToken);
        var stuckBefore = DateTimeOffset.UtcNow - DocumentReviewService.StuckAfter;
        return Results.Ok(new
        {
            id = document.Id,
            clientDocumentId = document.ClientDocumentId,
            documentType = document.DocumentType,
            state = document.State.ToString(),
            reviewReason = document.ReviewReason?.ToString(),
            stuck = IsStuck(document, stuckBefore),
            // The delete rule (design 6.11), so the workbench shows Delete without repeating it.
            canDelete = DocumentReviewService.DeleteCheck(
                document.DocumentType,
                document.State,
                document.AcumaticaAttachedAt,
                document.UploadedAt,
                stuckBefore) == DeleteOutcome.Deleted,
            // The change-invoice rule (design 6.13), so the workbench shows Change invoice without repeating it.
            canChangeInvoice = DocumentReviewService.ChangeInvoiceCheck(
                document.DocumentType,
                document.State,
                document.AcumaticaAttachedAt) == ChangeInvoiceOutcome.Changed,
            pageCount = document.PageCount,
            sizeBytes = document.SizeBytes,
            matchedReference = document.MatchedReference,
            matchedCustomerName = document.MatchedCustomerName,
            matchedBy = document.MatchedBy,
            acumaticaAttachPending = document.AcumaticaAttachPending,
            acumaticaAttachedAt = document.AcumaticaAttachedAt,
            uploadedBy = document.UploadedBy,
            uploadedAt = document.UploadedAt,
            scannerDeviceName = DeviceName(document, deviceNames),
            scannerStaffName = document.ScannerStaffName,
            resolvedBy = document.ResolvedBy,
            resolvedAt = document.ResolvedAt,
            reviewNote = document.ReviewNote,
            rejectedBy = document.RejectedBy,
            rejectedAt = document.RejectedAt,
            updatedAt = document.UpdatedAt,
            // Typed on the phone; for a general document also what Claude read (design 6.12).
            title = document.Title,
            note = document.Note,
            autoTitle = document.AutoTitle,
            summary = document.Summary,
            documentDate = document.DocumentDate,
            searchWords = document.SearchWords ?? [],
            hasThumbnail = document.ThumbnailBlobPath != null,
            extraction,
            unreadableReason,
            candidates,
            // Read-only, so the workbench can word the file-to dialog for the mode in force.
            acumaticaAttachMode = captureOptions.Value.AcumaticaAttachMode.ToString()
        });
    }

    private static async Task<IResult> FileAsync(
        Guid id,
        HttpContext httpContext,
        PvmDbContext dbContext,
        IDocumentStore documentStore,
        CancellationToken cancellationToken)
    {
        var document = await dbContext.CapturedDocuments
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new { item.DocumentType, item.BlobPath })
            .SingleOrDefaultAsync(cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        var pdf = await documentStore.OpenReadAsync(document.BlobPath, cancellationToken);
        httpContext.Response.Headers.ContentDisposition = $"inline; filename=\"{document.DocumentType}-{id:D}.pdf\"";
        return Results.Stream(pdf, "application/pdf");
    }

    private static async Task<IResult> ThumbnailAsync(
        Guid id,
        HttpContext httpContext,
        PvmDbContext dbContext,
        IDocumentStore documentStore,
        CancellationToken cancellationToken)
    {
        var path = await dbContext.CapturedDocuments
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => item.ThumbnailBlobPath)
            .SingleOrDefaultAsync(cancellationToken);
        if (path is null)
        {
            return Results.Problem("The document has no thumbnail.", statusCode: StatusCodes.Status404NotFound);
        }

        Stream jpeg;
        try
        {
            jpeg = await documentStore.OpenReadAsync(path, cancellationToken);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException
            or Azure.RequestFailedException { Status: 404 })
        {
            return Results.Problem("The document has no thumbnail.", statusCode: StatusCodes.Status404NotFound);
        }

        httpContext.Response.Headers.CacheControl = "private, max-age=300";
        return Results.Stream(jpeg, "image/jpeg");
    }

    private static async Task<IResult> InvoiceSearchAsync(
        string? q,
        IAcumaticaDocumentClient acumatica,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequest("The query 'q' is required.");
        }

        var results = await acumatica.SearchInvoicesAsync(q.Trim(), cancellationToken);
        return Results.Ok(results);
    }

    private static async Task<IResult> FileToAsync(
        Guid id,
        FileToRequest? request,
        CurrentAppUserAccessor currentUser,
        DocumentReviewService reviewService,
        CancellationToken cancellationToken)
    {
        var reference = request?.InvoiceReference?.Trim();
        if (string.IsNullOrEmpty(reference) || reference.Length > MaxReferenceLength)
        {
            return BadRequest($"'invoiceReference' is required, at most {MaxReferenceLength} characters.");
        }

        var user = CurrentEmail(currentUser);
        if (user is null)
        {
            return UserNotResolved();
        }

        var result = await reviewService.FileToAsync(id, reference, user, cancellationToken);
        return result.Outcome switch
        {
            FileToOutcome.Filed => Results.Ok(new
            {
                documentId = id,
                state = DocumentState.Filed.ToString(),
                matchedReference = result.Invoice!.ReferenceNumber,
                matchedCustomerName = result.Invoice.CustomerName
            }),
            FileToOutcome.NotFound => NotFound(),
            FileToOutcome.NotOpen => Conflict("The document is not on the review list; only a document in NeedsReview or Failed can be filed."),
            FileToOutcome.InvoiceNotFound => BadRequest($"Acumatica has no invoice with the reference '{reference}'."),
            _ => throw new InvalidOperationException("Unknown file-to outcome.")
        };
    }

    private static async Task<IResult> RejectAsync(
        Guid id,
        RejectRequest? request,
        CurrentAppUserAccessor currentUser,
        DocumentReviewService reviewService,
        CancellationToken cancellationToken)
    {
        var note = request?.Note?.Trim();
        if (string.IsNullOrEmpty(note) || note.Length > DocumentReviewService.MaxNoteLength)
        {
            return BadRequest($"'note' is required, at most {DocumentReviewService.MaxNoteLength} characters.");
        }

        var user = CurrentEmail(currentUser);
        if (user is null)
        {
            return UserNotResolved();
        }

        return await reviewService.RejectAsync(id, note, user, cancellationToken) switch
        {
            RejectOutcome.Rejected => Results.Ok(new { documentId = id, rejected = true }),
            RejectOutcome.NotFound => NotFound(),
            RejectOutcome.NotOpen => Conflict("The document is not open on the review list; only an unrejected document in NeedsReview or Failed can be rejected."),
            _ => throw new InvalidOperationException("Unknown reject outcome.")
        };
    }

    private static async Task<IResult> ReturnToReviewAsync(
        Guid id,
        RejectRequest? request,
        CurrentAppUserAccessor currentUser,
        DocumentReviewService reviewService,
        CancellationToken cancellationToken)
    {
        var note = request?.Note?.Trim();
        if (string.IsNullOrEmpty(note) || note.Length > DocumentReviewService.MaxNoteLength)
        {
            return BadRequest($"'note' is required, at most {DocumentReviewService.MaxNoteLength} characters.");
        }

        var user = CurrentEmail(currentUser);
        if (user is null)
        {
            return UserNotResolved();
        }

        return await reviewService.ReturnToReviewAsync(id, note, user, cancellationToken) switch
        {
            ReturnToReviewOutcome.Returned => Results.Ok(new { documentId = id, state = DocumentState.NeedsReview.ToString() }),
            ReturnToReviewOutcome.NotFound => NotFound(),
            ReturnToReviewOutcome.NotFiled => Conflict("The document is not filed; only a filed document can be sent back to review."),
            ReturnToReviewOutcome.AlreadyAttached => Conflict(
                "The PDF is already attached to the invoice in Acumatica. Remove the attachment in Acumatica by hand; "
                + "the document cannot be sent back to review from here."),
            _ => throw new InvalidOperationException("Unknown return-to-review outcome.")
        };
    }

    private static async Task<IResult> ProcessAgainAsync(
        Guid id,
        CurrentAppUserAccessor currentUser,
        DocumentReviewService reviewService,
        CancellationToken cancellationToken)
    {
        var user = CurrentEmail(currentUser);
        if (user is null)
        {
            return UserNotResolved();
        }

        return await reviewService.ProcessAgainAsync(id, user, cancellationToken) switch
        {
            ProcessAgainOutcome.Queued => Results.Accepted($"/api/documents/{id:D}", new { documentId = id, state = DocumentState.Received.ToString() }),
            ProcessAgainOutcome.NotFound => NotFound(),
            ProcessAgainOutcome.NotRetryable => Conflict(
                "Only a Failed document, or one stuck in Received or Processing for more than 30 minutes, can be processed again."),
            _ => throw new InvalidOperationException("Unknown process-again outcome.")
        };
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        DeleteRequest? request,
        CurrentAppUserAccessor currentUser,
        DocumentReviewService reviewService,
        CancellationToken cancellationToken)
    {
        var reason = request?.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > DocumentReviewService.MaxDeleteReasonLength)
        {
            return BadRequest($"'reason' is required, at most {DocumentReviewService.MaxDeleteReasonLength} characters.");
        }

        var user = CurrentEmail(currentUser);
        if (user is null)
        {
            return UserNotResolved();
        }

        var result = await reviewService.DeleteAsync(id, reason, user, cancellationToken);
        return result.Outcome switch
        {
            DeleteOutcome.Deleted => Results.Ok(new
            {
                documentId = id,
                deleted = true,
                warning = DeleteWarning(result),
                // "pdf" or "thumbnail": which file stayed behind, so the workbench titles the notice.
                warningFile = DeleteWarningFile(result)
            }),
            DeleteOutcome.NotFound => NotFound(),
            DeleteOutcome.Filed => Conflict(
                "The document is filed. Send it back to review first.",
                "document-filed"),
            DeleteOutcome.Attached => Conflict(
                "The PDF is already attached to the invoice in Acumatica; the document cannot be deleted.",
                "document-attached"),
            DeleteOutcome.Processing => Conflict(
                "The document was uploaded recently and is still processing. Wait until processing ends.",
                "document-processing"),
            _ => throw new InvalidOperationException("Unknown delete outcome.")
        };
    }

    /// <summary>The warning after a delete that left a file in storage, naming the file; null when none.</summary>
    public static string? DeleteWarning(DeleteResult result)
    {
        if (result.PdfDeleteFailed)
        {
            return "The document is deleted, but its PDF could not be removed from storage. The path is logged for the developer.";
        }

        return result.ThumbnailDeleteFailed
            ? "The scan is deleted. Its preview image could not be removed from storage."
            : null;
    }

    /// <summary>Which file stayed behind: "pdf" (it wins when both did), "thumbnail", or null.</summary>
    public static string? DeleteWarningFile(DeleteResult result)
        => result.PdfDeleteFailed ? "pdf" : result.ThumbnailDeleteFailed ? "thumbnail" : null;

    private static async Task<IResult> ChangeInvoiceAsync(
        Guid id,
        ChangeInvoiceRequest? request,
        CurrentAppUserAccessor currentUser,
        DocumentReviewService reviewService,
        CancellationToken cancellationToken)
    {
        var reference = request?.InvoiceReference?.Trim();
        if (string.IsNullOrEmpty(reference) || reference.Length > MaxReferenceLength)
        {
            return BadRequest($"'invoiceReference' is required, at most {MaxReferenceLength} characters.");
        }

        var reason = string.IsNullOrWhiteSpace(request?.Reason) ? null : request.Reason.Trim();
        if (reason is not null && reason.Length > MaxChangeReasonLength)
        {
            return BadRequest($"'reason' is optional, at most {MaxChangeReasonLength} characters.");
        }

        var user = CurrentEmail(currentUser);
        if (user is null)
        {
            return UserNotResolved();
        }

        var result = await reviewService.ChangeInvoiceAsync(id, reference, reason, user, cancellationToken);
        return result.Outcome switch
        {
            ChangeInvoiceOutcome.Changed => Results.Ok(new
            {
                documentId = id,
                state = DocumentState.Filed.ToString(),
                previousReference = result.PreviousReference,
                matchedReference = result.Invoice!.ReferenceNumber,
                matchedCustomerName = result.Invoice.CustomerName
            }),
            ChangeInvoiceOutcome.NotFound => NotFound(),
            ChangeInvoiceOutcome.NotAPod => Conflict(
                "Only a proof of delivery is linked to an invoice; a general document has no invoice.",
                "not-a-pod"),
            ChangeInvoiceOutcome.NotFiled => Conflict(
                "The document is not filed; only a filed document can be linked to another invoice. File it from the review list.",
                "document-not-filed"),
            ChangeInvoiceOutcome.Attached => Conflict(
                "The PDF is already attached to the invoice in Acumatica; the invoice cannot be changed from here.",
                "document-attached"),
            ChangeInvoiceOutcome.ChangedMeanwhile => Conflict(
                "Someone changed the invoice of this document meanwhile. Reload the page to see its invoice.",
                "document-changed"),
            ChangeInvoiceOutcome.InvoiceNotFound => BadRequest(
                $"Acumatica has no invoice with the reference '{reference}'.",
                "invoice-not-found"),
            ChangeInvoiceOutcome.SameInvoice => BadRequest(
                $"The document is already linked to {result.PreviousReference}.",
                "same-invoice"),
            _ => throw new InvalidOperationException("Unknown change-invoice outcome.")
        };
    }

    private static object SearchItem(CapturedDocumentEntity item, IReadOnlyDictionary<Guid, string> deviceNames) => new
    {
        id = item.Id,
        documentType = item.DocumentType,
        state = item.State.ToString(),
        reviewReason = item.ReviewReason?.ToString(),
        matchedReference = item.MatchedReference,
        matchedCustomerName = item.MatchedCustomerName,
        matchedBy = item.MatchedBy,
        acumaticaAttachPending = item.AcumaticaAttachPending,
        acumaticaAttachedAt = item.AcumaticaAttachedAt,
        pageCount = item.PageCount,
        uploadedBy = item.UploadedBy,
        uploadedAt = item.UploadedAt,
        scannerDeviceName = DeviceName(item, deviceNames),
        scannerStaffName = item.ScannerStaffName,
        resolvedBy = item.ResolvedBy,
        resolvedAt = item.ResolvedAt,
        rejectedAt = item.RejectedAt,
        // A general document shows its title and note in the mixed list (design 6.13).
        title = item.Title,
        autoTitle = item.AutoTitle,
        note = item.Note,
        hasThumbnail = item.ThumbnailBlobPath != null
    };

    /// <summary>The current names of the scanner devices that uploaded these documents (design 6.10).</summary>
    internal static async Task<IReadOnlyDictionary<Guid, string>> DeviceNamesAsync(
        PvmDbContext dbContext,
        IReadOnlyCollection<CapturedDocumentEntity> documents,
        CancellationToken cancellationToken)
    {
        var ids = documents
            .Where(document => document.ScannerDeviceId is not null)
            .Select(document => document.ScannerDeviceId!.Value)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await dbContext.ScannerDevices
            .AsNoTracking()
            .Where(device => ids.Contains(device.Id))
            .ToDictionaryAsync(device => device.Id, device => device.Name, cancellationToken);
    }

    internal static string? DeviceName(CapturedDocumentEntity document, IReadOnlyDictionary<Guid, string> deviceNames)
        => document.ScannerDeviceId is { } id && deviceNames.TryGetValue(id, out var name) ? name : null;

    private static bool IsStuck(CapturedDocumentEntity item, DateTimeOffset stuckBefore)
        => (item.State == DocumentState.Received || item.State == DocumentState.Processing)
            && item.UploadedAt < stuckBefore;

    /// <summary>
    /// Splits the stored extraction into the read fields, the unreadable reason, and the
    /// matcher's candidates (design 6.5 step 7). Unparseable JSON shows as no extraction.
    /// </summary>
    private static (JsonObject? Extraction, string? UnreadableReason, JsonArray Candidates) ReadExtraction(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (null, null, []);
        }

        JsonObject? fields;
        try
        {
            fields = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return (null, null, []);
        }

        if (fields is null)
        {
            return (null, null, []);
        }

        string? unreadable = null;
        if (fields.TryGetPropertyValue("unreadable", out var unreadableNode))
        {
            unreadable = unreadableNode is JsonValue value && value.TryGetValue<string>(out var reason) ? reason : "unknown";
            fields.Remove("unreadable");
        }

        var candidates = new JsonArray();
        if (fields.TryGetPropertyValue("candidates", out var candidatesNode))
        {
            fields.Remove("candidates");
            if (candidatesNode is JsonArray array)
            {
                candidates = array;
            }
        }

        return (fields.Count == 0 ? null : fields, unreadable, candidates);
    }

    internal static IResult BadRequest(string detail)
        => Results.Problem(detail, statusCode: StatusCodes.Status400BadRequest);

    private static IResult BadRequest(string detail, string code)
        => Results.Problem(
            detail,
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private static IResult Conflict(string detail)
        => Results.Problem(detail, statusCode: StatusCodes.Status409Conflict);

    private static IResult Conflict(string detail, string code)
        => Results.Problem(
            detail,
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private static IResult NotFound()
        => Results.Problem("The document was not found.", statusCode: StatusCodes.Status404NotFound);

    public sealed record FileToRequest(string? InvoiceReference);

    public sealed record RejectRequest(string? Note);

    public sealed record DeleteRequest(string? Reason);

    public sealed record ChangeInvoiceRequest(string? InvoiceReference, string? Reason);
}
