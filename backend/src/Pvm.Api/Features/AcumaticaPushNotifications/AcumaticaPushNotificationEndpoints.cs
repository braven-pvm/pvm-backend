using System.Text;
using Microsoft.Extensions.Options;
using Pvm.Infrastructure.Acumatica;

namespace Pvm.Api.Features.AcumaticaPushNotifications;

public static class AcumaticaPushNotificationEndpoints
{
    public static IEndpointRouteBuilder MapAcumaticaPushNotificationEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/webhooks/acumatica/invoice-changes", ReceiveAsync)
            .RequireRateLimiting("acumatica-webhook")
            .AllowAnonymous();
        return app;
    }

    private static async Task<IResult> ReceiveAsync(
        HttpRequest request,
        AcumaticaPushNotificationInbox inbox,
        IOptions<AcumaticaPushNotificationOptions> configuredOptions,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var options = configuredOptions.Value;
        if (!IsConfigured(options))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Acumatica push notifications are not configured.");
        }

        if (!request.Headers.TryGetValue(options.HeaderName, out var suppliedSecret)
            || !AcumaticaPushNotificationSecretValidator.IsValid(suppliedSecret, options.Secret!))
        {
            return Results.Unauthorized();
        }

        if (request.ContentType is null
            || !request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = "Content-Type must be application/json." });
        }

        string payload;
        try
        {
            payload = await ReadBodyAsync(request, options.MaxBodyBytes, cancellationToken);
        }
        catch (AcumaticaPushBodyTooLargeException)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        catch (DecoderFallbackException)
        {
            return Results.BadRequest(new { message = "The notification body must be valid UTF-8." });
        }

        AcumaticaPushNotification notification;
        try
        {
            notification = AcumaticaPushNotificationParser.Parse(payload);
        }
        catch (AcumaticaPushNotificationException exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }

        if (!Contains(options.AllowedCompanies, notification.CompanyId)
            || !Contains(options.AllowedQueries, notification.Query))
        {
            return Results.BadRequest(new { message = "The notification source is not allowed." });
        }

        var receipt = await inbox.ReceiveAsync(
            notification,
            payload,
            options.EnvironmentName,
            cancellationToken);
        loggerFactory.CreateLogger("AcumaticaPushNotifications").LogInformation(
            "integration.webhook.accepted EventId={EventId} NotificationId={NotificationId} Query={Query} Company={Company} Duplicate={Duplicate} Enqueued={Enqueued}",
            receipt.EventId,
            notification.Id,
            notification.Query,
            notification.CompanyId,
            receipt.Duplicate,
            receipt.MessageIds.Count);

        return Results.Accepted(value: new
        {
            receipt.EventId,
            receipt.Duplicate,
            Enqueued = receipt.MessageIds.Count
        });
    }

    private static bool IsConfigured(AcumaticaPushNotificationOptions options)
        => !string.IsNullOrWhiteSpace(options.HeaderName)
            && options.Secret?.Length >= 32
            && options.AllowedCompanies.Length > 0
            && options.AllowedQueries.Length > 0;

    private static bool Contains(IEnumerable<string> allowed, string value)
        => allowed.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));

    private static async Task<string> ReadBodyAsync(
        HttpRequest request,
        int maxBodyBytes,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > maxBodyBytes)
        {
            throw new AcumaticaPushBodyTooLargeException();
        }

        await using var content = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await request.Body.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (content.Length + read > maxBodyBytes)
            {
                throw new AcumaticaPushBodyTooLargeException();
            }

            await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return new UTF8Encoding(false, true).GetString(content.ToArray());
    }

    private sealed class AcumaticaPushBodyTooLargeException : Exception;
}
