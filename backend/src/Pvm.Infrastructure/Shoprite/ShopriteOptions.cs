namespace Pvm.Infrastructure.Shoprite;

public sealed class ShopriteOptions
{
    public required string BaseUrl { get; init; }

    public required string Username { get; init; }

    public required string Password { get; init; }

    public required string ContractId { get; init; }

    public required string UiUser { get; init; }
}
