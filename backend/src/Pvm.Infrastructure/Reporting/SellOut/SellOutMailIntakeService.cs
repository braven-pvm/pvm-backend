using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pvm.Application.Reporting.SellOut;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Reporting.SellOut;

/// <summary>Settings of the sell-out mailbox (configuration section "SellOutMail").</summary>
public sealed record SellOutMailOptions
{
    public const string SectionName = "SellOutMail";

    /// <summary>Off by default, so that no environment reads the mailbox until its job is set up.</summary>
    public bool Enabled { get; init; }

    /// <summary>The Entra tenant of PVM. Empty means: find it from the domain of the mailbox.</summary>
    public string? TenantId { get; init; }

    /// <summary>The app registration "pvm-sellout-mail". An access policy limits it to the mailbox.</summary>
    public string? ClientId { get; init; }

    public string? ClientSecret { get; init; }

    public string Mailbox { get; init; } = "reports@pvm.co.za";

    /// <summary>Mail from these domains loads, if Exchange Online also confirms the sender as internal.</summary>
    public string[] AllowedSenderDomains { get; init; } = ["pvm.co.za"];

    /// <summary>Mails that one run reads. The rest wait for the next run.</summary>
    public int MaxMailsPerRun { get; init; } = 25;
}

/// <summary>The outcome of one run of the mail intake.</summary>
public sealed record SellOutMailIntakeResult(int Mails, int Loaded, int Failed, int Refused);

/// <summary>
/// Reads the sell-out mailbox. Each attached report of a trusted sender goes through the same
/// import as an upload, with no preview. Then the mail moves to Loaded, Failed or Refused, so
/// that the next run does not read it again. Refer to docs/design/sell-out-import.md, section 5.2.
/// </summary>
public sealed class SellOutMailIntakeService(
    ISellOutMailbox mailbox,
    SellOutImportService importer,
    IOptions<SellOutMailOptions> options,
    ILogger<SellOutMailIntakeService> logger)
{
    private static readonly string[] FileExtensions = [".xls", ".xlsx", ".txt", ".csv"];

    private readonly SellOutMailOptions _options = options.Value;

    /// <summary>
    /// A sender is trusted when its address is in an allowed domain and Exchange Online signed
    /// it in as a user of the organisation. The second check stops mail from outside that only
    /// shows a pvm.co.za address.
    /// </summary>
    public static bool IsTrusted(SellOutMail mail, IReadOnlyCollection<string> allowedDomains)
    {
        ArgumentNullException.ThrowIfNull(mail);
        var at = mail.SenderAddress?.LastIndexOf('@') ?? -1;
        if (at < 0 || !string.Equals(mail.AuthenticatedAs, "Internal", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var domain = mail.SenderAddress![(at + 1)..];
        return allowedDomains.Any(allowed => string.Equals(domain, allowed.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public async Task<SellOutMailIntakeResult> RunAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            throw new InvalidOperationException("The sell-out mail intake is switched off (SellOutMail:Enabled is false).");
        }

        var mails = await mailbox.ListInboxAsync(Math.Clamp(_options.MaxMailsPerRun, 1, 200), cancellationToken);
        var (loaded, failed, refused) = (0, 0, 0);
        foreach (var mail in mails)
        {
            var folder = await HandleAsync(mail, cancellationToken);
            await mailbox.MoveAsync(mail.Id, folder, cancellationToken);
            switch (folder)
            {
                case SellOutMailFolders.Loaded:
                    loaded++;
                    break;
                case SellOutMailFolders.Failed:
                    failed++;
                    break;
                default:
                    refused++;
                    break;
            }
        }

        logger.LogInformation(
            "integration.run.completed RunType=sell-out-mail Mails={Mails} Loaded={Loaded} Failed={Failed} Refused={Refused}",
            mails.Count, loaded, failed, refused);
        return new SellOutMailIntakeResult(mails.Count, loaded, failed, refused);
    }

    private async Task<string> HandleAsync(SellOutMail mail, CancellationToken cancellationToken)
    {
        if (!IsTrusted(mail, _options.AllowedSenderDomains))
        {
            // The mail is not opened. Only the sender domain goes to the log, not the address.
            logger.LogWarning(
                "sell-out.mail.refused MailId={MailId} Reason=untrusted-sender SenderDomain={Domain} AuthAs={AuthAs}",
                mail.Id, Domain(mail.SenderAddress), mail.AuthenticatedAs ?? "(none)");
            return SellOutMailFolders.Refused;
        }

        var files = mail.HasAttachments
            ? (await mailbox.GetAttachmentsAsync(mail.Id, cancellationToken))
                .Where(file => FileExtensions.Contains(Path.GetExtension(file.Name), StringComparer.OrdinalIgnoreCase))
                .ToList()
            : [];
        if (files.Count == 0)
        {
            logger.LogWarning("sell-out.mail.refused MailId={MailId} Reason=no-report-file", mail.Id);
            return SellOutMailFolders.Refused;
        }

        var receivedOn = DateOnly.FromDateTime(mail.ReceivedAt.ToOffset(TimeSpan.FromHours(2)).DateTime);
        var allLoaded = true;
        foreach (var file in files)
        {
            var result = await importer.ImportAsync(
                new SellOutFile(file.Name, file.Content, receivedOn),
                $"mail:{mail.SenderAddress!.Trim().ToLowerInvariant()}",
                cancellationToken);

            // A file that is loaded already changes nothing, and is not a failure.
            var ok = result.Status == SellOutImportStatuses.Loaded
                || (result.Reason?.StartsWith("already-loaded", StringComparison.Ordinal) ?? false);
            allLoaded &= ok;
            logger.LogInformation(
                "sell-out.mail.file MailId={MailId} ImportId={ImportId} Status={Status} Reason={Reason}",
                mail.Id, result.ImportId, result.Status, result.Reason ?? "(none)");
        }

        return allLoaded ? SellOutMailFolders.Loaded : SellOutMailFolders.Failed;
    }

    private static string Domain(string? address) =>
        address is null || !address.Contains('@', StringComparison.Ordinal) ? "(none)" : address[(address.LastIndexOf('@') + 1)..];
}
