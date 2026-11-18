using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pvm.Application.Reporting.SellOut;

namespace Pvm.Infrastructure.Reporting.SellOut;

/// <summary>Reads the responses of Microsoft Graph for the sell-out mailbox.</summary>
public static class GraphMailParser
{
    /// <summary>The header in which Exchange Online records how it accepted the sender.</summary>
    public const string AuthAsHeader = "X-MS-Exchange-Organization-AuthAs";

    public static IReadOnlyList<SellOutMail> ParseMessages(string json)
    {
        using var document = JsonDocument.Parse(json);
        var mails = new List<SellOutMail>();
        foreach (var item in Values(document))
        {
            string? authAs = null;
            if (item.TryGetProperty("internetMessageHeaders", out var headers) && headers.ValueKind == JsonValueKind.Array)
            {
                authAs = headers.EnumerateArray()
                    .Where(header => string.Equals(Text(header, "name"), AuthAsHeader, StringComparison.OrdinalIgnoreCase))
                    .Select(header => Text(header, "value"))
                    .FirstOrDefault();
            }

            var sender = item.TryGetProperty("from", out var from) && from.TryGetProperty("emailAddress", out var address)
                ? Text(address, "address")
                : null;
            mails.Add(new SellOutMail(
                Text(item, "id") ?? throw new FormatException("A mail has no id."),
                sender,
                Text(item, "subject"),
                DateTimeOffset.Parse(Text(item, "receivedDateTime") ?? throw new FormatException("A mail has no receivedDateTime."), System.Globalization.CultureInfo.InvariantCulture),
                item.TryGetProperty("hasAttachments", out var has) && has.ValueKind == JsonValueKind.True,
                authAs?.Trim()));
        }

        return mails;
    }

    /// <summary>The file attachments. A link or an attached mail is not a file and is left out.</summary>
    public static IReadOnlyList<SellOutMailAttachment> ParseAttachments(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Values(document)
            .Where(item => Text(item, "@odata.type") == "#microsoft.graph.fileAttachment" && Text(item, "contentBytes") is not null)
            .Select(item => new SellOutMailAttachment(
                Path.GetFileName(Text(item, "name") ?? "attachment"),
                Text(item, "contentType"),
                Convert.FromBase64String(Text(item, "contentBytes")!)))
            .ToList();
    }

    public static string? FolderId(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("value", out _)
            ? Values(document).Select(item => Text(item, "id")).FirstOrDefault()
            : Text(document.RootElement, "id");
    }

    private static IEnumerable<JsonElement> Values(JsonDocument document) =>
        document.RootElement.TryGetProperty("value", out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Select(value => value.Clone()).ToList()
            : [];

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>
/// The sell-out mailbox through Microsoft Graph, with the app "pvm-sellout-mail". An Exchange
/// application access policy limits the app to this one mailbox.
/// </summary>
public sealed class GraphSellOutMailbox(HttpClient httpClient, IOptions<SellOutMailOptions> options) : ISellOutMailbox
{
    private const string Graph = "https://graph.microsoft.com/v1.0";
    private readonly SellOutMailOptions _options = options.Value;
    private readonly Dictionary<string, string> _folderIds = new(StringComparer.Ordinal);
    private string? _token;

    public async Task<IReadOnlyList<SellOutMail>> ListInboxAsync(int max, CancellationToken cancellationToken)
    {
        var select = "id,subject,from,receivedDateTime,hasAttachments,internetMessageHeaders";
        var json = await GetAsync($"{MailboxPath}/mailFolders/inbox/messages?$top={max}&$orderby=receivedDateTime%20asc&$select={select}", cancellationToken);
        return GraphMailParser.ParseMessages(json);
    }

    public async Task<IReadOnlyList<SellOutMailAttachment>> GetAttachmentsAsync(string mailId, CancellationToken cancellationToken) =>
        GraphMailParser.ParseAttachments(await GetAsync($"{MailboxPath}/messages/{Uri.EscapeDataString(mailId)}/attachments", cancellationToken));

    public async Task MoveAsync(string mailId, string folder, CancellationToken cancellationToken)
    {
        var folderId = await FolderIdAsync(folder, cancellationToken);
        using var request = await RequestAsync(HttpMethod.Post, $"{MailboxPath}/messages/{Uri.EscapeDataString(mailId)}/move", cancellationToken);
        request.Content = JsonContent.Create(new { destinationId = folderId });
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "move the mail");
    }

    private string MailboxPath => $"{Graph}/users/{Uri.EscapeDataString(_options.Mailbox)}";

    private async Task<string> FolderIdAsync(string folder, CancellationToken cancellationToken)
    {
        if (_folderIds.TryGetValue(folder, out var cached))
        {
            return cached;
        }

        var filter = Uri.EscapeDataString($"displayName eq '{folder}'");
        var id = GraphMailParser.FolderId(await GetAsync($"{MailboxPath}/mailFolders/inbox/childFolders?$filter={filter}", cancellationToken));
        if (id is null)
        {
            using var request = await RequestAsync(HttpMethod.Post, $"{MailboxPath}/mailFolders/inbox/childFolders", cancellationToken);
            request.Content = JsonContent.Create(new { displayName = folder });
            using var response = await httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, $"make the folder {folder}");
            id = GraphMailParser.FolderId(await response.Content.ReadAsStringAsync(cancellationToken));
        }

        _folderIds[folder] = id ?? throw new InvalidOperationException($"Graph gave no id for the folder {folder}.");
        return id;
    }

    private async Task<string> GetAsync(string uri, CancellationToken cancellationToken)
    {
        using var request = await RequestAsync(HttpMethod.Get, uri, cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "read the mailbox");
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<HttpRequestMessage> RequestAsync(HttpMethod method, string uri, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(cancellationToken));
        return request;
    }

    // One run lasts minutes, and a token lasts an hour, so the job asks for one token for each run.
    private async Task<string> TokenAsync(CancellationToken cancellationToken)
    {
        if (_token is not null)
        {
            return _token;
        }

        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new InvalidOperationException("SellOutMail ClientId and ClientSecret are required.");
        }

        var tenant = string.IsNullOrWhiteSpace(_options.TenantId) ? await TenantFromDomainAsync(cancellationToken) : _options.TenantId;
        using var response = await httpClient.PostAsync(
            $"https://login.microsoftonline.com/{Uri.EscapeDataString(tenant)}/oauth2/v2.0/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["scope"] = "https://graph.microsoft.com/.default",
                ["grant_type"] = "client_credentials",
            }),
            cancellationToken);
        await EnsureSuccessAsync(response, "sign in to Microsoft Graph");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        _token = document.RootElement.GetProperty("access_token").GetString();
        return _token ?? throw new InvalidOperationException("Microsoft Graph gave no access token.");
    }

    private async Task<string> TenantFromDomainAsync(CancellationToken cancellationToken)
    {
        var domain = _options.Mailbox[(_options.Mailbox.LastIndexOf('@') + 1)..];
        using var document = JsonDocument.Parse(await httpClient.GetStringAsync(
            $"https://login.microsoftonline.com/{Uri.EscapeDataString(domain)}/v2.0/.well-known/openid-configuration", cancellationToken));
        // The issuer is https://login.microsoftonline.com/{tenant id}/v2.0.
        return new Uri(document.RootElement.GetProperty("issuer").GetString()!).Segments[1].TrimEnd('/');
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var code = "(none)";
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            code = document.RootElement.TryGetProperty("error", out var error)
                ? error.ValueKind == JsonValueKind.Object ? error.GetProperty("code").GetString() ?? code : error.GetString() ?? code
                : code;
        }
        catch (JsonException)
        {
            // The body is not JSON. The status code is enough.
        }

        throw new HttpRequestException($"Microsoft Graph answered {(int)response.StatusCode} ({code}) when the job tried to {operation}.");
    }
}
