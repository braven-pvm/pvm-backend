namespace Pvm.Application.Submissions;

public enum PayloadArchiveKind
{
    AcumaticaSource,
    CanonicalInvoice,
    ShopriteRequest,
    ShopriteResponse
}

public sealed record PayloadArchiveWrite(
    PayloadArchiveKind Kind,
    string Path,
    string ContentType,
    string Content);

public sealed record PayloadArchiveRecord(
    PayloadArchiveKind Kind,
    string Location,
    string Sha256Hash,
    string ContentType,
    long ByteCount,
    DateTimeOffset CreatedAt);

public interface IPayloadArchive
{
    Task<PayloadArchiveRecord> WriteAsync(
        PayloadArchiveWrite payload,
        CancellationToken cancellationToken);

    Task<string> ReadVerifiedAsync(
        PayloadArchiveRecord payload,
        CancellationToken cancellationToken);
}
