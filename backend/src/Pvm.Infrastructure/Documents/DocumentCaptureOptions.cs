namespace Pvm.Infrastructure.Documents;

public sealed class DocumentCaptureOptions
{
    public const string SectionName = "DocumentCapture";

    /// <summary>FileSystem (local development and tests) or AzureBlob.</summary>
    public string Provider { get; set; } = "FileSystem";

    public string ContainerName { get; set; } = "documents";

    public string? ServiceUri { get; set; }

    public string FileSystemRoot { get; set; } = ".pvm/documents";

    /// <summary>
    /// The largest PDF the intake accepts: 15 MB, which keeps the base64 request to Claude well
    /// under its 32 MB limit. The extractor's own pre-call size guard stays as a second check.
    /// </summary>
    public long MaxUploadBytes { get; set; } = 15L * 1024 * 1024;

    /// <summary>Shadow records the Acumatica attachment it would make; Write makes it (design 6.6).</summary>
    public AcumaticaAttachMode AcumaticaAttachMode { get; set; } = AcumaticaAttachMode.Shadow;
}

public enum AcumaticaAttachMode
{
    Shadow,
    Write,
}
