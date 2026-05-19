using Pvm.Api.Auth;

namespace Pvm.Api.Features.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/me", (CurrentAppUserAccessor currentUser) =>
            currentUser.User is null
                ? Results.Forbid()
                : Results.Ok(new CurrentUserResponse(
                    currentUser.User.Id,
                    currentUser.User.EntraObjectId,
                    currentUser.User.Email,
                    currentUser.User.DisplayName,
                    currentUser.User.Roles.ToArray())))
            .RequireAuthorization("Invoices.Read");

        return app;
    }
}

public sealed record CurrentUserResponse(
    Guid Id,
    string? EntraObjectId,
    string Email,
    string? DisplayName,
    IReadOnlyList<string> Roles);
