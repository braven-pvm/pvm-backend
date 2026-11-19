using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Pvm.Application.Documents;
using Pvm.Application.Messaging;
using Pvm.Infrastructure.Documents;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Api.Features.Documents;

/// <summary>Who uploaded a document: a workbench user, or a scanner device and its staff name.</summary>
public sealed record DocumentUploader(
    string UploadedBy,
    Guid? ScannerDeviceId = null,
    Guid? ScannerStaffId = null,
    string? ScannerStaffName = null);

/// <summary>The uploader, or the error response that stops the upload.</summary>
public sealed record UploaderResolution(DocumentUploader? Uploader, IResult? Error)
{
    public static UploaderResolution Of(DocumentUploader uploader) => new(uploader, null);

    public static UploaderResolution Refuse(IResult error) => new(null, error);
}

/// <summary>
/// The one upload pipeline behind <c>POST /api/documents</c> and <c>POST /api/device/documents</c>
/// (design 6.1, 6.10, 6.12): the multipart contract, the idempotent client document ID, the size
/// limit, the PDF check, then the blob (and the optional thumbnail), and the row and its message in
/// one transaction.
/// </summary>
/// <remarks>
/// Optional fields: <c>title</c> (at most 120 characters, GENERAL only; 400 for another type),
/// <c>note</c> (at most 1000 characters, any type), and <c>thumbnail</c> (a JPEG of at most
/// <see cref="MaxThumbnailBytes"/>). Blank text counts as missing. A thumbnail that is too large
/// or not a JPEG is left out and logged; the document itself is still stored, so a scan is never
/// lost for its preview.
/// </remarks>
public sealed class DocumentIntakeService(
    PvmDbContext dbContext,
    IIntegrationCommandQueue commandQueue,
    IDocumentStore documentStore,
    IEnumerable<IDocumentType> documentTypes,
    IOptions<DocumentCaptureOptions> options,
    ILogger<DocumentIntakeService> logger)
{
    /// <summary>The largest thumbnail the upload keeps: 200 KB.</summary>
    public const int MaxThumbnailBytes = 200 * 1024;

    private static readonly byte[] PdfSignature = Encoding.ASCII.GetBytes("%PDF-");

    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    /// <param name="resolveUploader">
    /// Called after the form is valid and the client document ID is new; it may refuse the upload
    /// (for example an unknown staff ID) before anything is stored.
    /// </param>
    public async Task<IResult> UploadAsync(
        HttpRequest request,
        Func<IFormCollection, CancellationToken, Task<UploaderResolution>> resolveUploader,
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

        var title = GeneralDocumentFields.Clean(form["title"].ToString());
        var note = GeneralDocumentFields.Clean(form["note"].ToString());
        if (title is not null && !DocumentTypeCodes.IsGeneral(documentType.Code))
        {
            return BadRequest("The form field 'title' is only for the document type GENERAL.");
        }

        if (title is { Length: > GeneralDocumentFields.MaxTitleLength })
        {
            return BadRequest($"The form field 'title' must be at most {GeneralDocumentFields.MaxTitleLength} characters.");
        }

        if (note is { Length: > GeneralDocumentFields.MaxNoteLength })
        {
            return BadRequest($"The form field 'note' must be at most {GeneralDocumentFields.MaxNoteLength} characters.");
        }

        var existing = await FindAsync(clientDocumentId, cancellationToken);
        if (existing is not null)
        {
            return Results.Ok(Receipt(existing));
        }

        var resolution = await resolveUploader(form, cancellationToken);
        if (resolution.Uploader is null)
        {
            return resolution.Error ?? throw new InvalidOperationException("The uploader was neither resolved nor refused.");
        }

        var uploader = resolution.Uploader;
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

        string? thumbnailPath = null;
        var thumbnail = form.Files.GetFile("thumbnail");
        if (thumbnail is not null && thumbnail.Length > 0)
        {
            if (thumbnail.Length <= MaxThumbnailBytes && await StartsWithAsync(thumbnail, JpegSignature, cancellationToken))
            {
                thumbnailPath = ThumbnailPath(blobPath);
                await using var jpeg = thumbnail.OpenReadStream();
                await documentStore.SaveAsync(thumbnailPath, jpeg, "image/jpeg", cancellationToken);
            }
            else
            {
                logger.LogWarning(
                    "document.upload.thumbnail-ignored DocumentId={DocumentId} SizeBytes={SizeBytes} Reason={Reason}",
                    documentId,
                    thumbnail.Length,
                    thumbnail.Length > MaxThumbnailBytes ? "too-large" : "not-jpeg");
            }
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
            UploadedBy = uploader.UploadedBy,
            UploadedAt = now,
            ScannerDeviceId = uploader.ScannerDeviceId,
            ScannerStaffId = uploader.ScannerStaffId,
            ScannerStaffName = uploader.ScannerStaffName,
            Title = title,
            Note = note,
            SearchText = GeneralDocumentFields.SearchText(title, note),
            ThumbnailBlobPath = thumbnailPath,
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
            var winner = await FindAsync(clientDocumentId, cancellationToken)
                ?? throw new InvalidOperationException("The concurrent document upload could not be read back.");
            return Results.Ok(Receipt(winner));
        }

        return Results.Created(
            $"/api/documents/{documentId:D}",
            new { documentId = document.Id, state = document.State.ToString(), thumbnailStored = thumbnailPath is not null });
    }

    /// <summary>The thumbnail's path: the PDF's path with <c>.thumb.jpg</c> in place of <c>.pdf</c>.</summary>
    public static string ThumbnailPath(string pdfBlobPath)
        => (pdfBlobPath.EndsWith(".pdf", StringComparison.Ordinal) ? pdfBlobPath[..^4] : pdfBlobPath) + ".thumb.jpg";

    private Task<CapturedDocumentEntity?> FindAsync(Guid clientDocumentId, CancellationToken cancellationToken)
        => dbContext.CapturedDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(document => document.ClientDocumentId == clientDocumentId, cancellationToken);

    private static Task<bool> StartsWithPdfSignatureAsync(IFormFile file, CancellationToken cancellationToken)
        => StartsWithAsync(file, PdfSignature, cancellationToken);

    private static async Task<bool> StartsWithAsync(IFormFile file, byte[] signature, CancellationToken cancellationToken)
    {
        var header = new byte[signature.Length];
        await using var stream = file.OpenReadStream();
        await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        return header.AsSpan().SequenceEqual(signature);
    }

    private static object Receipt(CapturedDocumentEntity document)
        => new { documentId = document.Id, state = document.State.ToString() };

    internal static IResult BadRequest(string detail)
        => Results.Problem(detail, statusCode: StatusCodes.Status400BadRequest);
}
