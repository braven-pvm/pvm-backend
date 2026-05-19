using Microsoft.EntityFrameworkCore;

namespace Pvm.Infrastructure.Persistence;

public static class AuthSchemaInitializer
{
    public static async Task EnsureAuthSchemaAsync(
        this PvmDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            create table if not exists app_users (
                "Id" uuid primary key,
                "EntraObjectId" character varying(128) null,
                "Email" character varying(320) not null,
                "DisplayName" character varying(256) null,
                "Status" character varying(64) not null,
                "CreatedAt" timestamp with time zone not null,
                "UpdatedAt" timestamp with time zone not null,
                "LastLoginAt" timestamp with time zone null
            );

            create unique index if not exists "IX_app_users_Email"
                on app_users ("Email");

            create unique index if not exists "IX_app_users_EntraObjectId"
                on app_users ("EntraObjectId");

            create table if not exists app_user_roles (
                "Id" uuid primary key,
                "AppUserId" uuid not null references app_users ("Id") on delete cascade,
                "Role" character varying(64) not null,
                "GrantedByAppUserId" uuid null references app_users ("Id") on delete set null,
                "GrantedAt" timestamp with time zone not null
            );

            create unique index if not exists "IX_app_user_roles_AppUserId_Role"
                on app_user_roles ("AppUserId", "Role");

            create table if not exists app_user_audit_events (
                "Id" uuid primary key,
                "ActorAppUserId" uuid null references app_users ("Id") on delete set null,
                "TargetAppUserId" uuid not null references app_users ("Id") on delete cascade,
                "Action" character varying(128) not null,
                "BeforeJson" jsonb null,
                "AfterJson" jsonb null,
                "CreatedAt" timestamp with time zone not null
            );

            create index if not exists "IX_app_user_audit_events_TargetAppUserId"
                on app_user_audit_events ("TargetAppUserId");
            """,
            cancellationToken);
    }
}
