using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Pvm.Infrastructure.Documents;

namespace Pvm.Api.Auth;

/// <summary>
/// The <c>DeviceKey</c> scheme for scanner phones (design 6.10): reads <c>Authorization: Device &lt;key&gt;</c>
/// and finds the device by the SHA-256 hash of the key. It is never the default scheme and adds no
/// role claims, so it cannot satisfy a workbench policy; only <c>Documents.Device</c> names it.
/// </summary>
public sealed class DeviceKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DeviceKey";
    public const string AuthorizationPrefix = "Device ";
    public const string DeviceIdClaim = "pvm_scanner_device_id";
    public const string DeviceNameClaim = "pvm_scanner_device_name";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.FirstOrDefault();
        if (header is null || !header.StartsWith(AuthorizationPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var key = header[AuthorizationPrefix.Length..].Trim();
        var devices = Context.RequestServices.GetRequiredService<ScannerDeviceService>();
        var device = await devices.AuthenticateAsync(key, Context.RequestAborted);
        if (device is null)
        {
            return AuthenticateResult.Fail("The device key is not valid.");
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(DeviceIdClaim, device.Id.ToString("D")),
                new Claim(DeviceNameClaim, device.Name)
            ],
            SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}

public static class DeviceKeyAuthenticationExtensions
{
    public static AuthenticationBuilder AddDeviceKey(this AuthenticationBuilder builder)
        => builder.AddScheme<AuthenticationSchemeOptions, DeviceKeyAuthenticationHandler>(
            DeviceKeyAuthenticationHandler.SchemeName,
            _ => { });
}
