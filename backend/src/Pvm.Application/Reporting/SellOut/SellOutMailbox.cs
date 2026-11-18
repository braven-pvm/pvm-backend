namespace Pvm.Application.Reporting.SellOut;

/// <summary>A mail in the sell-out mailbox.</summary>
/// <param name="AuthenticatedAs">
/// What Exchange Online recorded about the sender: "Internal" for a signed-in user of the
/// organisation. A mail from outside that only shows a pvm.co.za address is not "Internal".
/// </param>
public sealed record SellOutMail(
    string Id,
    string? SenderAddress,
    string? Subject,
    DateTimeOffset ReceivedAt,
    bool HasAttachments,
    string? AuthenticatedAs);

/// <summary>A file attached to a mail.</summary>
public sealed record SellOutMailAttachment(string Name, string? ContentType, byte[] Content);

/// <summary>The folders that the intake files each mail in, after it read the mail.</summary>
public static class SellOutMailFolders
{
    public const string Loaded = "Loaded";
    public const string Failed = "Failed";
    public const string Refused = "Refused";
}

/// <summary>The mailbox that receives sell-out files.</summary>
public interface ISellOutMailbox
{
    /// <summary>The mails in the inbox, oldest first. Mails in the sub-folders are done.</summary>
    Task<IReadOnlyList<SellOutMail>> ListInboxAsync(int max, CancellationToken cancellationToken);

    Task<IReadOnlyList<SellOutMailAttachment>> GetAttachmentsAsync(string mailId, CancellationToken cancellationToken);

    /// <summary>Moves the mail to a sub-folder of the inbox. Makes the folder if it does not exist.</summary>
    Task MoveAsync(string mailId, string folder, CancellationToken cancellationToken);
}
