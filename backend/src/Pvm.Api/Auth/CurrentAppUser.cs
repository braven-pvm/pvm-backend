namespace Pvm.Api.Auth;

public sealed record CurrentAppUser(
    Guid Id,
    string? EntraObjectId,
    string Email,
    string? DisplayName,
    IReadOnlyCollection<string> Roles)
{
    public bool IsInRole(string role)
        => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
}
