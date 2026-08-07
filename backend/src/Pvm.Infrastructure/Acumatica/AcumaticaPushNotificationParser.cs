using System.Text.Json;

namespace Pvm.Infrastructure.Acumatica;

public static class AcumaticaPushNotificationParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static AcumaticaPushNotification Parse(string payload)
    {
        AcumaticaPushNotification notification;
        try
        {
            notification = JsonSerializer.Deserialize<AcumaticaPushNotification>(payload, SerializerOptions)
                ?? throw new AcumaticaPushNotificationException("The notification body is required.");
        }
        catch (JsonException exception)
        {
            throw new AcumaticaPushNotificationException("The notification body is not valid JSON.", exception);
        }

        if (notification.Id == Guid.Empty)
        {
            throw new AcumaticaPushNotificationException("The notification transaction ID is required.");
        }

        if (notification.TimeStamp <= 0)
        {
            throw new AcumaticaPushNotificationException("The notification timestamp is required.");
        }

        if (string.IsNullOrWhiteSpace(notification.Query) || string.IsNullOrWhiteSpace(notification.CompanyId))
        {
            throw new AcumaticaPushNotificationException(
                "The notification query and company are required.");
        }

        return notification with
        {
            Query = notification.Query.Trim(),
            CompanyId = notification.CompanyId.Trim(),
            Inserted = notification.Inserted ?? [],
            Deleted = notification.Deleted ?? []
        };
    }
}

public sealed class AcumaticaPushNotificationException : Exception
{
    public AcumaticaPushNotificationException(string message) : base(message)
    {
    }

    public AcumaticaPushNotificationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
