using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Persistence;

public sealed class DatabaseMigrationRunnerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Fresh_database_applies_all_migrations()
    {
        await using var db = CreateDbContext();

        await DatabaseMigrationRunner.MigrateAsync(db);

        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.Equal(5, applied.Count());
        Assert.Contains(DatabaseMigrationRunner.LegacyBaselineMigration, applied);
        Assert.True(await TableExistsAsync(db, "submission_operations"));
        Assert.True(await TableExistsAsync(db, "payload_archives"));
        Assert.True(await TableExistsAsync(db, "submission_operation_transitions"));
        Assert.True(await TableExistsAsync(db, "integration_outbox_messages"));
        Assert.True(await TableExistsAsync(db, "integration_message_deliveries"));
        Assert.True(await TableExistsAsync(db, "integration_runs"));
    }

    [Fact]
    public async Task Existing_legacy_database_is_baselined_then_migrated_without_data_loss()
    {
        await using var db = CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(DatabaseMigrationRunner.LegacyBaselineMigration);

        var candidate = NewCandidate();
        db.InvoiceCandidates.Add(candidate);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""drop table "__EFMigrationsHistory";""");

        await DatabaseMigrationRunner.MigrateAsync(db);

        Assert.Equal(candidate.Id, (await db.InvoiceCandidates.SingleAsync()).Id);
        Assert.True(await TableExistsAsync(db, "submission_operations"));
        Assert.True(await ColumnExistsAsync(db, "invoice_submission_attempts", "SubmissionOperationId"));
        Assert.True(await TableExistsAsync(db, "payload_archives"));
        Assert.True(await TableExistsAsync(db, "submission_operation_transitions"));
        Assert.True(await TableExistsAsync(db, "integration_outbox_messages"));
        Assert.True(await TableExistsAsync(db, "integration_message_deliveries"));
        Assert.True(await TableExistsAsync(db, "integration_runs"));
        Assert.Equal(5, (await db.Database.GetAppliedMigrationsAsync()).Count());
    }

    private PvmDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PvmDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new PvmDbContext(options);
    }

    private static Task<bool> TableExistsAsync(PvmDbContext db, string tableName)
        => db.Database.SqlQuery<bool>(
                $"select to_regclass({"public." + tableName}) is not null as \"Value\"")
            .SingleAsync();

    private static Task<bool> ColumnExistsAsync(PvmDbContext db, string tableName, string columnName)
        => db.Database.SqlQuery<bool>(
                $"""
                select exists (
                    select 1
                    from information_schema.columns
                    where table_schema = 'public'
                        and table_name = {tableName}
                        and column_name = {columnName}
                ) as "Value"
                """)
            .SingleAsync();

    private static InvoiceCandidateEntity NewCandidate()
        => new()
        {
            Id = Guid.NewGuid(),
            AcumaticaInvoiceId = "ACU-LEGACY-1",
            InvoiceNumber = "INV-LEGACY-1",
            CustomerAccount = "SHOPRITE",
            IdempotencyKey = "legacy-key",
            Status = "Ready",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
}
