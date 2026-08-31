using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

namespace Pvm.Infrastructure.Shoprite;

/// <summary>
/// Adds the Layer 7 headers that Shoprite requires. Their integration guide states that a
/// call fails without the Authorization, ContractID, and UIUser headers.
/// </summary>
public sealed class ShopriteLayer7Handler(IOptions<ShopriteOptions> options) : DelegatingHandler
{
    private readonly ShopriteOptions _options = options.Value;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Apply(request, _options);
        return base.SendAsync(request, cancellationToken);
    }

    internal static void Apply(HttpRequestMessage request, ShopriteOptions options)
    {
        if (!options.UseLayer7Headers)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.Username) || string.IsNullOrWhiteSpace(options.Password))
        {
            throw new InvalidOperationException("Shoprite:Username and Shoprite:Password are required.");
        }

        if (string.IsNullOrWhiteSpace(options.ContractId))
        {
            throw new InvalidOperationException("Shoprite:ContractId is required for Layer 7 calls.");
        }

        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{options.Username.Trim()}:{options.Password.Trim()}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        request.Headers.Remove("ContractID");
        request.Headers.Add("ContractID", options.ContractId.Trim());

        request.Headers.Remove("UIUser");
        request.Headers.Add("UIUser", options.EffectiveUiUser.Trim());
    }
}
