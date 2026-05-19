using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pvm.Api.Auth;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Api.Features.Admin;

public static class AdminUserEndpoints
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapAdminUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/users")
            .RequireAuthorization("Admin");

        group.MapGet("/", ListUsersAsync);
        group.MapPost("/", CreateUserAsync);
        group.MapPut("/{id:guid}/roles", UpdateRolesAsync);
        group.MapPut("/{id:guid}/status", UpdateStatusAsync);

        return app;
    }

    private static async Task<IResult> ListUsersAsync(
        PvmDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        var users = await dbContext.AppUsers
            .AsNoTracking()
            .OrderBy(user => user.Email)
            .ToListAsync(cancellationToken);
        var userIds = users.Select(user => user.Id).ToArray();
        var roles = await dbContext.AppUserRoles
            .AsNoTracking()
            .Where(role => userIds.Contains(role.AppUserId))
            .GroupBy(role => role.AppUserId)
            .ToDictionaryAsync(
                group => group.Key,
                group => group.Select(role => role.Role).Order().ToArray(),
                cancellationToken);

        return Results.Ok(users.Select(user => ToResponse(user, roles)).ToArray());
    }

    private static async Task<IResult> CreateUserAsync(
        CreateAppUserRequest request,
        PvmDbContext dbContext,
        CurrentAppUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        var email = NormalizeEmail(request.Email);
        if (email is null)
        {
            return Results.BadRequest(new { message = "Email is required." });
        }

        var roles = NormalizeRoles(request.Roles);
        if (roles.Count == 0)
        {
            return Results.BadRequest(new { message = "At least one valid role is required." });
        }

        var now = DateTimeOffset.UtcNow;
        var user = await dbContext.AppUsers.SingleOrDefaultAsync(
            appUser => appUser.Email == email,
            cancellationToken);
        if (user is null)
        {
            user = new AppUserEntity
            {
                Id = Guid.NewGuid(),
                Email = email,
                DisplayName = Normalize(request.DisplayName),
                Status = "Active",
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.AppUsers.Add(user);
        }
        else
        {
            user.DisplayName = Normalize(request.DisplayName) ?? user.DisplayName;
            user.Status = "Active";
            user.UpdatedAt = now;
        }

        await ReplaceRolesAsync(dbContext, user, roles, currentUser.User?.Id, now, cancellationToken);
        AddAudit(dbContext, currentUser.User?.Id, user.Id, "user-preauthorized", null, new { email, roles, user.Status }, now);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToResponse(user, roles));
    }

    private static async Task<IResult> UpdateRolesAsync(
        Guid id,
        UpdateAppUserRolesRequest request,
        PvmDbContext dbContext,
        CurrentAppUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        var user = await dbContext.AppUsers.FindAsync([id], cancellationToken);
        if (user is null)
        {
            return Results.NotFound(new { id, message = "App user not found." });
        }

        var roles = NormalizeRoles(request.Roles);
        if (roles.Count == 0)
        {
            return Results.BadRequest(new { message = "At least one valid role is required." });
        }

        var previousRoles = await dbContext.AppUserRoles
            .Where(role => role.AppUserId == id)
            .Select(role => role.Role)
            .Order()
            .ToArrayAsync(cancellationToken);
        if (currentUser.User?.Id == id && !roles.Contains(AppRoles.Admin, StringComparer.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = "You cannot remove your own Admin role." });
        }

        if (previousRoles.Contains(AppRoles.Admin, StringComparer.OrdinalIgnoreCase)
            && !roles.Contains(AppRoles.Admin, StringComparer.OrdinalIgnoreCase)
            && await IsLastActiveAdminAsync(dbContext, id, cancellationToken))
        {
            return Results.BadRequest(new { message = "At least one active Admin user is required." });
        }

        var now = DateTimeOffset.UtcNow;

        await ReplaceRolesAsync(dbContext, user, roles, currentUser.User?.Id, now, cancellationToken);
        user.UpdatedAt = now;
        AddAudit(
            dbContext,
            currentUser.User?.Id,
            user.Id,
            "roles-updated",
            new { roles = previousRoles },
            new { roles },
            now);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToResponse(user, roles));
    }

    private static async Task<IResult> UpdateStatusAsync(
        Guid id,
        UpdateAppUserStatusRequest request,
        PvmDbContext dbContext,
        CurrentAppUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        var user = await dbContext.AppUsers.FindAsync([id], cancellationToken);
        if (user is null)
        {
            return Results.NotFound(new { id, message = "App user not found." });
        }

        var status = Normalize(request.Status);
        if (status is not ("Active" or "Disabled"))
        {
            return Results.BadRequest(new { message = "Status must be Active or Disabled." });
        }

        if (currentUser.User?.Id == id && status == "Disabled")
        {
            return Results.BadRequest(new { message = "You cannot disable your own user." });
        }

        var roles = await dbContext.AppUserRoles
            .Where(role => role.AppUserId == id)
            .Select(role => role.Role)
            .Order()
            .ToArrayAsync(cancellationToken);
        if (status == "Disabled"
            && roles.Contains(AppRoles.Admin, StringComparer.OrdinalIgnoreCase)
            && await IsLastActiveAdminAsync(dbContext, id, cancellationToken))
        {
            return Results.BadRequest(new { message = "At least one active Admin user is required." });
        }

        var previousStatus = user.Status;
        user.Status = status;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        AddAudit(
            dbContext,
            currentUser.User?.Id,
            user.Id,
            "status-updated",
            new { status = previousStatus },
            new { status },
            user.UpdatedAt);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToResponse(user, roles));
    }

    private static async Task<bool> IsLastActiveAdminAsync(
        PvmDbContext dbContext,
        Guid adminUserId,
        CancellationToken cancellationToken)
        => !await (
                from role in dbContext.AppUserRoles
                join user in dbContext.AppUsers on role.AppUserId equals user.Id
                where role.Role == AppRoles.Admin
                    && role.AppUserId != adminUserId
                    && user.Status == "Active"
                select role.Id)
            .AnyAsync(
                cancellationToken);

    private static async Task ReplaceRolesAsync(
        PvmDbContext dbContext,
        AppUserEntity user,
        IReadOnlyCollection<string> roles,
        Guid? actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.AppUserRoles
            .Where(role => role.AppUserId == user.Id)
            .ToListAsync(cancellationToken);
        dbContext.AppUserRoles.RemoveRange(existing);
        foreach (var role in roles)
        {
            dbContext.AppUserRoles.Add(new AppUserRoleEntity
            {
                Id = Guid.NewGuid(),
                AppUserId = user.Id,
                Role = role,
                GrantedByAppUserId = actorId,
                GrantedAt = now
            });
        }
    }

    private static AppUserResponse ToResponse(
        AppUserEntity user,
        IReadOnlyDictionary<Guid, string[]> rolesByUser)
        => ToResponse(
            user,
            rolesByUser.TryGetValue(user.Id, out var roles) ? roles : []);

    private static AppUserResponse ToResponse(
        AppUserEntity user,
        IReadOnlyCollection<string> roles)
        => new(
            user.Id,
            user.EntraObjectId,
            user.Email,
            user.DisplayName,
            user.Status,
            roles.Order().ToArray(),
            user.CreatedAt,
            user.UpdatedAt,
            user.LastLoginAt);

    private static IReadOnlyList<string> NormalizeRoles(IEnumerable<string> roles)
        => roles
            .Select(NormalizeRole)
            .Where(role => role is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .Order()
            .ToArray();

    private static string? NormalizeRole(string? role)
    {
        var normalized = Normalize(role);
        return AppRoles.All.SingleOrDefault(valid => string.Equals(valid, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeEmail(string? value)
        => Normalize(value)?.ToLowerInvariant();

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void AddAudit(
        PvmDbContext dbContext,
        Guid? actorId,
        Guid targetId,
        string action,
        object? before,
        object? after,
        DateTimeOffset now)
        => dbContext.AppUserAuditEvents.Add(new AppUserAuditEventEntity
        {
            Id = Guid.NewGuid(),
            ActorAppUserId = actorId,
            TargetAppUserId = targetId,
            Action = action,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before, SerializerOptions),
            AfterJson = after is null ? null : JsonSerializer.Serialize(after, SerializerOptions),
            CreatedAt = now
        });
}
