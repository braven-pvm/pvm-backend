using Pvm.Api.Auth;
using Pvm.Application.Documents;

namespace Pvm.Api.Features.Documents;

/// <summary>
/// Document capture intake (design 6.1, 6.2):
/// <list type="bullet">
///   <item><c>POST /api/documents</c> — the phone app uploads one PDF per scanned document.
///   The PDF goes to the document store, a <c>Received</c> row and its processing message are
///   written in one transaction. A known <c>clientDocumentId</c> returns the existing document.</item>
///   <item><c>GET /api/documents/types</c> — the document types the app may offer.</item>
/// </list>
/// The upload pipeline itself is <see cref="DocumentIntakeService"/>, shared with the scanner
/// devices' <c>POST /api/device/documents</c>.
/// </summary>
public static class DocumentIntakeEndpoints
{
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

    private static Task<IResult> UploadAsync(
        HttpRequest request,
        DocumentIntakeService intake,
        CurrentAppUserAccessor currentUser,
        CancellationToken cancellationToken)
        => intake.UploadAsync(
            request,
            (_, _) =>
            {
                var uploadedBy = DocumentReviewEndpoints.CurrentEmail(currentUser);
                return Task.FromResult(uploadedBy is null
                    ? UploaderResolution.Refuse(DocumentReviewEndpoints.UserNotResolved())
                    : UploaderResolution.Of(new DocumentUploader(uploadedBy)));
            },
            cancellationToken);
}
