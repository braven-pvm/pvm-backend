namespace Pvm.Infrastructure.PayloadArchive;

public sealed class PayloadArchiveOptions
{
    public const string SectionName = "PayloadArchive";

    public string Provider { get; set; } = "FileSystem";
    public string ContainerName { get; set; } = "payloads";
    public string? ServiceUri { get; set; }
    public string FileSystemRoot { get; set; } = ".pvm/payloads";
}
