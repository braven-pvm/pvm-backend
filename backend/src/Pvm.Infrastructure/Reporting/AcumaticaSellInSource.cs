using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
using Pvm.Application.Reporting;
using Pvm.Domain.Reporting;
using Pvm.Infrastructure.Acumatica;

namespace Pvm.Infrastructure.Reporting;

/// <summary>
/// Reads one posting period of the inquiry PVM-Sales-Lines over OData. The filter on
/// TranPeriodID runs on the server. The read never calls the inquiry without a filter: an
/// inquiry computes all its rows before it applies $top, which can take minutes.
/// Refer to docs/runbooks/acumatica-gi-pvm-sales-lines.md.
/// </summary>
public sealed class AcumaticaSellInSource(
    HttpClient httpClient,
    IOptions<AcumaticaOptions> acumaticaOptions,
    IOptions<SellInSyncOptions> sellInOptions) : ISellInSource
{
    private readonly AcumaticaOptions _acumatica = acumaticaOptions.Value;
    private readonly SellInSyncOptions _sellIn = sellInOptions.Value;

    public async Task<SellInPeriodRead> ReadPeriodAsync(PostingPeriod period, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_acumatica.BaseUrl)
            || string.IsNullOrWhiteSpace(_acumatica.Company)
            || string.IsNullOrWhiteSpace(_acumatica.Username)
            || string.IsNullOrWhiteSpace(_acumatica.Password))
        {
            throw new InvalidOperationException("Acumatica BaseUrl, Company and credentials are required for the sell-in load.");
        }

        var filter = Uri.EscapeDataString($"TranPeriodID eq '{period.DisplayId}'");
        var uri = $"{_acumatica.BaseUrl.TrimEnd('/')}/odata/{Uri.EscapeDataString(_acumatica.Company)}/"
            + $"{Uri.EscapeDataString(_sellIn.InquiryName)}?$filter={filter}&$format=json";

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        // OData uses Basic authentication as user@company, not the contract API session cookie.
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_acumatica.Username}@{_acumatica.Company}:{_acumatica.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(10, _sellIn.ReadTimeoutSeconds)));

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new SellInReadException(
                $"Acumatica refused the inquiry {_sellIn.InquiryName} with {(int)response.StatusCode}. "
                + "Check the integration user's access to the inquiry.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new SellInReadException(
                $"Acumatica answered {(int)response.StatusCode} for period {period.DisplayId} of {_sellIn.InquiryName}.");
        }

        var json = await response.Content.ReadAsStringAsync(timeout.Token);
        var (rows, skipped) = SellInRowParser.Parse(json);
        return new SellInPeriodRead(period, json, rows, skipped);
    }
}
