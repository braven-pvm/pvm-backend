using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Api.Auth;

public sealed class AppUserClaimsMiddleware(
    RequestDelegate next,
    IOptions<AuthOptions> authOptions)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(
        HttpContext context,
        PvmDbContext dbContext,
        CurrentAppUserAccessor currentUserAccessor)
    {
        var cancellationToken = context.RequestAborted;
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        await dbContext.EnsureAuthSchemaAsync(cancellationToken);

        var identity = ResolveIdentity(context.User);
        if (identity.Email is null && identity.ObjectId is null)
        {
            await next(context);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var user = await FindUserAsync(dbContext, identity, cancellationToken);
        var isBootstrapAdmin = IsBootstrapAdmin(identity);

        if (user is null && isBootstrapAdmin)
        {
            user = new AppUserEntity
            {
                Id = Guid.NewGuid(),
                EntraObjectId = identity.ObjectId,
                Email = identity.Email ?? $"{identity.ObjectId}@bootstrap.local",
                DisplayName = identity.DisplayName,
                Status = "Active",
                CreatedAt = now,
                UpdatedAt = now,
                LastLoginAt = now
            };

            dbContext.AppUsers.Add(user);
            dbContext.AppUserRoles.Add(new AppUserRoleEntity
            {
                Id = Guid.NewGuid(),
                AppUserId = user.Id,
                Role = AppRoles.Admin,
                GrantedAt = now
            });
            dbContext.AppUserAuditEvents.Add(new AppUserAuditEventEntity
            {
                Id = Guid.NewGuid(),
                TargetAppUserId = user.Id,
                Action = "bootstrap-admin-created",
                AfterJson = JsonSerializer.Serialize(
                    new { user.Email, user.EntraObjectId, role = AppRoles.Admin },
                    SerializerOptions),
                CreatedAt = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else if (user is not null)
        {
            var changed = false;
            if (!string.IsNullOrWhiteSpace(identity.ObjectId) && user.EntraObjectId != identity.ObjectId)
            {
                user.EntraObjectId = identity.ObjectId;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(identity.Email) && user.Email != identity.Email)
            {
                user.Email = identity.Email;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(identity.DisplayName) && user.DisplayName != identity.DisplayName)
            {
                user.DisplayName = identity.DisplayName;
                changed = true;
            }

            if (user.LastLoginAt is null || now - user.LastLoginAt.Value > TimeSpan.FromMinutes(5))
            {
                user.LastLoginAt = now;
                user.UpdatedAt = now;
                changed = true;
            }

            if (changed)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        if (user is not null && user.Status == "Active")
        {
            var roles = await dbContext.AppUserRoles
                .Where(role => role.AppUserId == user.Id)
                .Select(role => role.Role)
                .ToArrayAsync(cancellationToken);

            if (roles.Length > 0 && context.User.Identity is ClaimsIdentity claimsIdentity)
            {
                claimsIdentity.AddClaim(new Claim(AppAuthClaims.AppUserId, user.Id.ToString()));
                foreach (var role in roles)
                {
                    claimsIdentity.AddClaim(new Claim(AppAuthClaims.Role, role));
                    claimsIdentity.AddClaim(new Claim(ClaimTypes.Role, role));
                }

                currentUserAccessor.User = new CurrentAppUser(
                    user.Id,
                    user.EntraObjectId,
                    user.Email,
                    user.DisplayName,
                    roles);
            }
        }

        await next(context);
    }

    private bool IsBootstrapAdmin(ResolvedIdentity identity)
    {
        var options = authOptions.Value;
        return (identity.Email is not null
                && options.BootstrapAdminEmails.Contains(identity.Email, StringComparer.OrdinalIgnoreCase))
            || (identity.ObjectId is not null
                && options.BootstrapAdminObjectIds.Contains(identity.ObjectId, StringComparer.OrdinalIgnoreCase));
    }

    private static async Task<AppUserEntity?> FindUserAsync(
        PvmDbContext dbContext,
        ResolvedIdentity identity,
        CancellationToken cancellationToken)
    {
        if (identity.ObjectId is not null)
        {
            var byObjectId = await dbContext.AppUsers.SingleOrDefaultAsync(
                user => user.EntraObjectId == identity.ObjectId,
                cancellationToken);
            if (byObjectId is not null)
            {
                return byObjectId;
            }
        }

        return identity.Email is null
            ? null
            : await dbContext.AppUsers.SingleOrDefaultAsync(
                user => user.Email == identity.Email,
                cancellationToken);
    }

    private static ResolvedIdentity ResolveIdentity(ClaimsPrincipal principal)
    {
        var objectId = principal.FindFirstValue("oid")
            ?? principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier");
        var email = principal.FindFirstValue("preferred_username")
            ?? principal.FindFirstValue("upn")
            ?? principal.FindFirstValue(ClaimTypes.Email);
        var displayName = principal.FindFirstValue("name")
            ?? principal.FindFirstValue(ClaimTypes.Name);

        return new ResolvedIdentity(
            Normalize(objectId),
            Normalize(email)?.ToLowerInvariant(),
            Normalize(displayName));
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ResolvedIdentity(
        string? ObjectId,
        string? Email,
        string? DisplayName);
}
