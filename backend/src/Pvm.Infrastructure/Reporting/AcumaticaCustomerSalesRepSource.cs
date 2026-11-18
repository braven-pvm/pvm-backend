using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Pvm.Application.Reporting;
using Pvm.Infrastructure.Acumatica;

namespace Pvm.Infrastructure.Reporting;

/// <summary>
/// Reads the default salesperson of every customer from the contract API entity Customer,
/// a page at a time. The read signs in, reads and signs out. It changes nothing in Acumatica.
/// </summary>
public sealed class AcumaticaCustomerSalesRepSource(
    HttpClient httpClient,
    IOptions<AcumaticaOptions> acumaticaOptions,
    IOptions<SellInSyncOptions> sellInOptions) : ICustomerSalesRepSource
{
    private const int PageSize = 500;

    private readonly AcumaticaOptions _acumatica = acumaticaOptions.Value;
    private readonly SellInSyncOptions _sellIn = sellInOptions.Value;

    public async Task<IReadOnlyList<CustomerSalesRep>> ReadAllAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_acumatica.BaseUrl)
            || string.IsNullOrWhiteSpace(_acumatica.Username)
            || string.IsNullOrWhiteSpace(_acumatica.Password)
            || string.IsNullOrWhiteSpace(_acumatica.EndpointName)
            || string.IsNullOrWhiteSpace(_acumatica.EndpointVersion))
        {
            throw new InvalidOperationException("Acumatica BaseUrl, endpoint and credentials are required for the customer list.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(10, _sellIn.ReadTimeoutSeconds)));

        var cookie = await SignInAsync(timeout.Token);
        try
        {
            var customers = new List<CustomerSalesRep>();
            for (var skip = 0; ; skip += PageSize)
            {
                using var request = SessionRequest(HttpMethod.Get, PageUri(skip), cookie);
                using var response = await httpClient.SendAsync(request, timeout.Token);
                EnsureSuccess(response, "customer list read");
                var page = CustomerSalesRepParser.ParsePage(await response.Content.ReadAsStringAsync(timeout.Token));
                customers.AddRange(page);
                if (page.Count < PageSize)
                {
                    return customers;
                }
            }
        }
        finally
        {
            using var logout = SessionRequest(HttpMethod.Post, BuildUri("entity/auth/logout"), cookie);
            using var _ = await httpClient.SendAsync(logout, CancellationToken.None);
        }
    }

    private async Task<string?> SignInAsync(CancellationToken cancellationToken)
    {
        var login = new Dictionary<string, string>
        {
            ["name"] = _acumatica.Username!,
            ["password"] = _acumatica.Password!
        };
        if (!string.IsNullOrWhiteSpace(_acumatica.Company))
        {
            login["company"] = _acumatica.Company;
        }

        if (!string.IsNullOrWhiteSpace(_acumatica.Branch))
        {
            login["branch"] = _acumatica.Branch;
        }

        using var response = await httpClient.PostAsJsonAsync(BuildUri("entity/auth/login"), login, cancellationToken);
        EnsureSuccess(response, "sign-in");
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            return null;
        }

        var baseUri = BuildUri(string.Empty);
        var cookies = new CookieContainer();
        foreach (var setCookie in setCookies)
        {
            cookies.SetCookies(baseUri, setCookie);
        }

        var header = cookies.GetCookieHeader(baseUri);
        return string.IsNullOrWhiteSpace(header) ? null : header;
    }

    private Uri PageUri(int skip)
    {
        var select = Uri.EscapeDataString("CustomerID,Salespersons/SalespersonID,Salespersons/Name,Salespersons/Default");
        return BuildUri($"entity/{Uri.EscapeDataString(_acumatica.EndpointName)}/{Uri.EscapeDataString(_acumatica.EndpointVersion)}/Customer"
            + $"?$select={select}&$expand=Salespersons&$top={PageSize}&$skip={skip}");
    }

    private Uri BuildUri(string relativePath) => new(new Uri(_acumatica.BaseUrl!.TrimEnd('/') + "/"), relativePath);

    private static HttpRequestMessage SessionRequest(HttpMethod method, Uri uri, string? cookie)
    {
        var request = new HttpRequestMessage(method, uri);
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        return request;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new SellInReadException($"Acumatica answered {(int)response.StatusCode} to the {operation}.");
        }
    }
}
