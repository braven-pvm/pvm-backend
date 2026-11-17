using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Pvm.Api.Auth;
using Pvm.Application.Documents;
using Pvm.Application.Messaging;
using Pvm.Infrastructure.Documents;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Api.Features.Documents;

/// <summary>
/// Document capture intake (design 6.1, 6.2):
/// <list type="bullet">
///   <item><c>POST /api/documents</c> — the phone app uploads one PDF per scanned document.
///   The PDF goes to the document store, a <c>Received</c> row and its processing message are
///   written in one transaction. A known <c>clientDocumentId</c> returns the existing document.</item>
///   <item><c>GET /api/documents/types</c> — the document types the app may offer.</item>
/// </list>
/// </summary>
public static class DocumentIntakeEndpoints
{
    private static readonly byte[] PdfSignature = Encoding.ASCII.GetBytes("%PDF-");

    public static IEndpointRouteBuilder MapDocumentIntakeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/documents");

        group.MapPost("", UploadAsync)
            .RequireAuthorization(Pvm.Api.Auth.ServiceCollectionExtensions.DocumentsCapturePolicy)
            .DisableAntiforgery();

        group.MapGet("/types", GetTypes)
            .RequireAuthorization("Invoices.Read");

        return app;
    }

    private static IResult GetTypes(IEnumerable<IDocumentType> documentTypes)
        => Results.Ok(documentTypes.Select(type => new { code = type.Code, displayName = type.DisplayName }));

    private static async Task<IResult> UploadAsync(
        HttpRequest request,
        PvmDbContext dbContext,
        IIntegrationCommandQueue commandQueue,
        IDocumentStore documentStore,
        IEnumerable<IDocumentType> documentTypes,
        IOptions<DocumentCaptureOptions> options,
        CurrentAppUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return BadRequest("The upload must be multipart/form-data.");
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
        {
            return BadRequest("The form field 'file' must hold the PDF.");
        }

        if (!Guid.TryParse(form["clientDocumentId"], out var clientDocumentId) || clientDocumentId == Guid.Empty)
        {
            return BadRequest("The form field 'clientDocumentId' must be a GUID.");
        }

        if (!int.TryParse(form["pageCount"], out var pageCount) || pageCount < 1)
        {
            return BadRequest("The form field 'pageCount' must be a whole number of 1 or more.");
        }

        var documentType = documentTypes.FirstOrDefault(type =>
            string.Equals(type.Code, form["documentType"], StringComparison.OrdinalIgnoreCase));
        if (documentType is null)
        {
            return BadRequest($"The document type '{form["documentType"]}' is not known.");
        }

        var existing = await FindAsync(dbContext, clientDocumentId, cancellationToken);
        if (existing is not null)
        {
            return Results.Ok(Receipt(existing));
        }

        var captureOptions = options.Value;
        if (file.Length > captureOptions.MaxUploadBytes)
        {
            return Results.Problem(
                $"The file is larger than the limit of {captureOptions.MaxUploadBytes} bytes.",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        if (!await StartsWithPdfSignatureAsync(file, cancellationToken))
        {
            return BadRequest("The file is not a PDF.");
        }

        var now = DateTimeOffset.UtcNow;
        var documentId = Guid.NewGuid();
        var blobPath = $"{captureOptions.ContainerName}/{documentType.Code}/{now:yyyy}/{now:MM}/{documentId:D}.pdf";

        // Blob first, then the row and its message in one transaction: a row never points at a missing PDF.
        StoredDocument stored;
        await using (var pdf = file.OpenReadStream())
        {
            stored = await documentStore.SaveAsync(blobPath, pdf, cancellationToken);
        }

        var document = new CapturedDocumentEntity
        {
            Id = documentId,
            ClientDocumentId = clientDocumentId,
            DocumentType = documentType.Code,
            State = DocumentState.Received,
            BlobPath = blobPath,
            PageCount = pageCount,
            SizeBytes = stored.SizeBytes,
            Sha256 = stored.Sha256,
            UploadedBy = currentUser.User?.Email ?? "unknown",
            UploadedAt = now,
            UpdatedAt = now
        };

        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            dbContext.CapturedDocuments.Add(document);
            await dbContext.SaveChangesAsync(cancellationToken);
            await commandQueue.EnqueueAsync(
                IntegrationQueues.DocumentProcessing,
                IntegrationMessageTypes.DocumentReceivedV1,
                new ProcessCapturedDocumentMessage(documentId),
                documentId.ToString("D"),
                cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        })
        {
            // A concurrent upload with the same client document ID won the insert. Its PDF is the
            // one on record; the blob this request wrote stays unreferenced.
            dbContext.ChangeTracker.Clear();
            var winner = await FindAsync(dbContext, clientDocumentId, cancellationToken)
                ?? throw new InvalidOperationException("The concurrent document upload could not be read back.");
            return Results.Ok(Receipt(winner));
        }

        return Results.Created($"/api/documents/{documentId:D}", Receipt(document));
    }

    private static Task<CapturedDocumentEntity?> FindAsync(
        PvmDbContext dbContext,
        Guid clientDocumentId,
        CancellationToken cancellationToken)
        => dbContext.CapturedDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(document => document.ClientDocumentId == clientDocumentId, cancellationToken);

    private static async Task<bool> StartsWithPdfSignatureAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var header = new byte[PdfSignature.Length];
        await using var stream = file.OpenReadStream();
        await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        return header.AsSpan().SequenceEqual(PdfSignature);
    }

    private static object Receipt(CapturedDocumentEntity document)
        => new { documentId = document.Id, state = document.State.ToString() };

    private static IResult BadRequest(string detail)
        => Results.Problem(detail, statusCode: StatusCodes.Status400BadRequest);
}
