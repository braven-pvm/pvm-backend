namespace Pvm.Api.Features.Admin;

public sealed record AppUserResponse(
    Guid Id,
    string? EntraObjectId,
    string Email,
    string? DisplayName,
    string Status,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastLoginAt);

public sealed record CreateAppUserRequest(
    string Email,
    string? DisplayName,
    IReadOnlyList<string> Roles);

public sealed record UpdateAppUserRolesRequest(
    IReadOnlyList<string> Roles);

public sealed record UpdateAppUserStatusRequest(
    string Status);
