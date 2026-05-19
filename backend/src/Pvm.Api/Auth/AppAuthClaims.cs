using System.Security.Claims;

namespace Pvm.Api.Auth;

public static class AppAuthClaims
{
    public const string AppUserId = "pvm_app_user_id";
    public const string Role = ClaimTypes.Role;
}
