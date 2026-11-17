using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Pvm.Api.Auth;
using Pvm.Application.Documents;
using Pvm.Infrastructure.Documents;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Api.Features.Documents;

/// <summary>
/// The workbench's Documents page and review list (design 6.8, 6.9):
/// <list type="bullet">
///   <item><c>GET /api/documents</c> — search, newest first, pages of 50.</item>
///   <item><c>GET /api/documents/review</c> — the open review list, oldest first: NeedsReview and
///   Failed documents that are not rejected, and Received or Processing ones that are stuck.</item>
///   <item><c>GET /api/documents/{id}</c> and <c>/{id}/file</c> — one document and its PDF.</item>
///   <item><c>GET /api/documents/review/invoice-search</c>, <c>POST /{id}/file-to</c> and
///   <c>POST /{id}/reject</c> — the review actions.</item>
///   <item><c>POST /{id}/return-to-review</c> — takes a wrongly filed document, not yet attached in
///   Acumatica, back to the review list.</item>
/// </list>
/// </summary>
public static class DocumentReviewEndpoints
{
    public const int PageSize = 50;

    private const int MaxReferenceLength = 64;

    public static IEndpointRouteBuilder MapDocumentReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/documents");

        group.MapGet("", SearchAsync).RequireAuthorization("Invoices.Read");
        group.MapGet("/review", ReviewListAsync).RequireAuthorization("Invoices.Read");
        group.MapGet("/review/invoice-search", InvoiceSearchAsync).RequireAuthorization("Invoices.Write");
        group.MapGet("/{id:guid}", DetailAsync).RequireAuthorization("Invoices.Read");
        group.MapGet("/{id:guid}/file", FileAsync).RequireAuthorization("Invoices.Read");
        group.MapPost("/{id:guid}/file-to", FileToAsync).RequireAuthorization("Invoices.Write");
        group.MapPost("/{id:guid}/reject", RejectAsync).RequireAuthorization("Invoices.Write");
        group.MapPost("/{id:guid}/return-to-review", ReturnToReviewAsync).RequireAuthorization("Invoices.Write");

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
        string? invoice,
        string? client,
        string? type,
        string? state,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? page,
        CancellationToken cancellationToken)
    {
        var query = dbContext.CapturedDocuments.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(invoice))
        {
            var reference = invoice.Trim().ToUpperInvariant();
            query = query.Where(item => item.MatchedReference != null && item.MatchedReference.ToUpper() == reference);
        }

        if (!string.IsNullOrWhiteSpace(client))
        {
            var name = client.Trim().ToLowerInvariant();
            query = query.Where(item => item.MatchedCustomerName != null && item.MatchedCustomerName.ToLower().Contains(name));
        }

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

        return Results.Ok(new
        {
            items = items.Select(SearchItem),
            page = pageNumber,
            pageSize = PageSize,
            total
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
        var stuckBefore = DateTimeOffset.UtcNow - DocumentReviewService.StuckAfter;
        return Results.Ok(new
        {
            id = document.Id,
            clientDocumentId = document.ClientDocumentId,
            documentType = document.DocumentType,
            state = document.State.ToString(),
            reviewReason = document.ReviewReason?.ToString(),
            stuck = IsStuck(document, stuckBefore),
            pageCount = document.PageCount,
            sizeBytes = document.SizeBytes,
            matchedReference = document.MatchedReference,
            matchedCustomerName = document.MatchedCustomerName,
            matchedBy = document.MatchedBy,
            acumaticaAttachPending = document.AcumaticaAttachPending,
            acumaticaAttachedAt = document.AcumaticaAttachedAt,
            uploadedBy = document.UploadedBy,
            uploadedAt = document.UploadedAt,
            resolvedBy = document.ResolvedBy,
            resolvedAt = document.ResolvedAt,
            reviewNote = document.ReviewNote,
            rejectedBy = document.RejectedBy,
            rejectedAt = document.RejectedAt,
            updatedAt = document.UpdatedAt,
            extraction,
            unreadableReason,
            candidates
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

    private static object SearchItem(CapturedDocumentEntity item) => new
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
        resolvedBy = item.ResolvedBy,
        resolvedAt = item.ResolvedAt,
        rejectedAt = item.RejectedAt
    };

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

    private static IResult BadRequest(string detail)
        => Results.Problem(detail, statusCode: StatusCodes.Status400BadRequest);

    private static IResult Conflict(string detail)
        => Results.Problem(detail, statusCode: StatusCodes.Status409Conflict);

    private static IResult NotFound()
        => Results.Problem("The document was not found.", statusCode: StatusCodes.Status404NotFound);

    public sealed record FileToRequest(string? InvoiceReference);

    public sealed record RejectRequest(string? Note);
}
