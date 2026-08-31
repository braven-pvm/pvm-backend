namespace Pvm.Infrastructure.Shoprite;

public sealed record ShopriteOptions
{
    public string? BaseUrl { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }

    /// <summary>
    /// Sends the Layer 7 headers. The Shoprite gateway host
    /// (`externalservices.shopriteholdings.co.za/b2bservice/api`) requires them. The older
    /// supplier-services host redirects any request that carries an Authorization header,
    /// so this stays off for that host.
    /// </summary>
    public bool UseLayer7Headers { get; init; }

    /// <summary>
    /// Layer 7 contract identifier. Shoprite uses the same value for QA and production.
    /// </summary>
    public string? ContractId { get; init; }

    /// <summary>
    /// Layer 7 UIUser header. Defaults to the configured username when it is not set.
    /// </summary>
    public string? UiUser { get; init; }

    /// <summary>
    /// Acknowledges downloaded orders with PUT VendorOrder?action=A. Shoprite stops
    /// providing new orders when acknowledgement does not happen.
    /// </summary>
    public bool AcknowledgeOrders { get; init; }

    public string EffectiveUiUser => string.IsNullOrWhiteSpace(UiUser) ? Username ?? string.Empty : UiUser;
}
