namespace Pvm.Infrastructure.Acumatica;

public sealed class AcumaticaPushNotificationOptions
{
    public const string SectionName = "AcumaticaPushNotifications";

    public string EnvironmentName { get; set; } = "Development";
    public string HeaderName { get; set; } = "X-PVM-Acumatica-Webhook-Secret";
    public string? Secret { get; set; }
    public string[] AllowedCompanies { get; set; } = [];
    public string[] AllowedQueries { get; set; } = [];
    public int MaxBodyBytes { get; set; } = 65_536;
    public int RateLimitPerMinute { get; set; } = 120;
}
