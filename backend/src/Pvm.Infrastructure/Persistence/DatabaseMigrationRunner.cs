using Microsoft.EntityFrameworkCore;

namespace Pvm.Infrastructure.Persistence;

public static class DatabaseMigrationRunner
{
    public const string LegacyBaselineMigration = "20260728130123_InitialSchema";

    private static readonly string[] LegacyTables =
    [
        "app_user_audit_events",
        "app_user_roles",
        "app_users",
        "audit_events",
        "invoice_candidates",
        "invoice_submission_attempts",
        "shoprite_item_mappings",
        "shoprite_purchase_order_lines",
        "shoprite_purchase_orders",
        "shoprite_uom_mappings"
    ];

    public static async Task MigrateAsync(
        PvmDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        if (await TableExistsAsync(dbContext, "invoice_candidates", cancellationToken)
            && !await HasMigrationHistoryAsync(dbContext, cancellationToken))
        {
            await ValidateLegacyBaselineAsync(dbContext, cancellationToken);
            await RecordLegacyBaselineAsync(dbContext, cancellationToken);
        }

        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    private static async Task ValidateLegacyBaselineAsync(
        PvmDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var missingTables = new List<string>();
        foreach (var table in LegacyTables)
        {
            if (!await TableExistsAsync(dbContext, table, cancellationToken))
            {
                missingTables.Add(table);
            }
        }

        if (missingTables.Count > 0)
        {
            throw new InvalidOperationException(
                $"The existing database cannot be adopted as '{LegacyBaselineMigration}'. "
                + $"Missing tables: {string.Join(", ", missingTables)}.");
        }

        if (!await ColumnExistsAsync(
                dbContext,
                "invoice_candidates",
                "MatchedShopritePurchaseOrderId",
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"The existing database cannot be adopted as '{LegacyBaselineMigration}'. "
                + "Column invoice_candidates.MatchedShopritePurchaseOrderId is missing.");
        }
    }

    private static async Task RecordLegacyBaselineAsync(
        PvmDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            $"""
            create table if not exists "__EFMigrationsHistory" (
                "MigrationId" character varying(150) not null,
                "ProductVersion" character varying(32) not null,
                constraint "PK___EFMigrationsHistory" primary key ("MigrationId")
            );

            insert into "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            values ('{LegacyBaselineMigration}', '10.0.8')
            on conflict ("MigrationId") do nothing;
            """,
            cancellationToken);
    }

    private static async Task<bool> HasMigrationHistoryAsync(
        PvmDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(dbContext, "__EFMigrationsHistory", cancellationToken))
        {
            return false;
        }

        return await dbContext.Database.SqlQuery<bool>(
                $"""
                select exists (
                    select 1 from "__EFMigrationsHistory"
                ) as "Value"
                """)
            .SingleAsync(cancellationToken);
    }

    private static Task<bool> TableExistsAsync(
        PvmDbContext dbContext,
        string tableName,
        CancellationToken cancellationToken)
        => dbContext.Database.SqlQuery<bool>(
                $"select to_regclass({"public." + tableName}) is not null as \"Value\"")
            .SingleAsync(cancellationToken);

    private static Task<bool> ColumnExistsAsync(
        PvmDbContext dbContext,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
        => dbContext.Database.SqlQuery<bool>(
                $"""
                select exists (
                    select 1
                    from information_schema.columns
                    where table_schema = 'public'
                        and table_name = {tableName}
                        and column_name = {columnName}
                ) as "Value"
                """)
            .SingleAsync(cancellationToken);
}
