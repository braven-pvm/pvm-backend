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

    /// <summary>
    /// The API's public https address that a scanner phone calls, written into the setup QR code
    /// (design 6.10). Set per environment; never taken from the request, because request headers
    /// can be spoofed into an enrolment QR. Empty or invalid: the setup-code endpoints refuse.
    /// </summary>
    public string? DeviceApiBaseUrl { get; set; }

    /// <summary>The <see cref="DeviceApiBaseUrl"/> without a trailing slash, when it is an absolute, credential-free https URL.</summary>
    public bool TryGetDeviceApiBaseUrl(out string baseUrl)
    {
        baseUrl = string.Empty;
        if (!Uri.TryCreate(DeviceApiBaseUrl?.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        baseUrl = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return true;
    }
}

public enum AcumaticaAttachMode
{
    Shadow,
    Write,
}
