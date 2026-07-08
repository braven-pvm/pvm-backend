using Microsoft.EntityFrameworkCore;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Persistence;

public sealed class InvoicePersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Invoice_candidate_idempotency_key_is_unique()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();

        db.InvoiceCandidates.Add(NewCandidate("key-1", "INV001"));
        db.InvoiceCandidates.Add(NewCandidate("key-1", "INV002"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Invoice_submission_attempt_requires_existing_invoice_candidate()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();

        db.InvoiceSubmissionAttempts.Add(NewAttempt(Guid.NewGuid()));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Shoprite_purchase_order_number_is_unique()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();

        db.ShopritePurchaseOrders.Add(NewPurchaseOrder("PO121"));
        db.ShopritePurchaseOrders.Add(NewPurchaseOrder("PO121"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Shoprite_purchase_order_lines_are_deleted_with_purchase_order()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();

        var order = NewPurchaseOrder("PO122");
        order.Lines.Add(new ShopritePurchaseOrderLineEntity
        {
            Id = Guid.NewGuid(),
            LineNumber = 1,
            Gtin = "06001197181125"
        });
        db.ShopritePurchaseOrders.Add(order);
        await db.SaveChangesAsync();

        db.ShopritePurchaseOrders.Remove(order);
        await db.SaveChangesAsync();

        Assert.Empty(await db.ShopritePurchaseOrderLines.ToListAsync());
    }

    [Fact]
    public async Task Purchase_order_schema_initializer_backfills_existing_invoice_database()
    {
        await using var db = CreateDbContext();
        await CreatePrePurchaseOrderSchemaAsync(db);

        await db.Database.EnsureCreatedAsync();

        Assert.False(await TableExistsAsync(db, "shoprite_purchase_orders"));

        await db.EnsureShopritePurchaseOrderSchemaAsync();

        Assert.True(await TableExistsAsync(db, "shoprite_purchase_orders"));
        Assert.True(await TableExistsAsync(db, "shoprite_purchase_order_lines"));
        Assert.True(await ColumnExistsAsync(db, "invoice_candidates", "MatchedShopritePurchaseOrderId"));

        var order = NewPurchaseOrder("PO123");
        var candidate = NewCandidate("key-2", "INV003");
        candidate.ShopritePurchaseOrderNumber = order.PurchaseOrderNumber;
        candidate.MatchedShopritePurchaseOrderId = order.Id;

        db.ShopritePurchaseOrders.Add(order);
        db.InvoiceCandidates.Add(candidate);

        await db.SaveChangesAsync();

        var savedCandidate = await db.InvoiceCandidates.SingleAsync(candidate => candidate.InvoiceNumber == "INV003");
        Assert.Equal(order.Id, savedCandidate.MatchedShopritePurchaseOrderId);
    }

    private PvmDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PvmDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        return new PvmDbContext(options);
    }

    private static async Task CreatePrePurchaseOrderSchemaAsync(PvmDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            create table invoice_candidates (
                "Id" uuid primary key,
                "AcumaticaInvoiceId" text not null,
                "InvoiceNumber" text not null,
                "CustomerAccount" text not null,
                "CustomerLocation" text null,
                "ShopritePurchaseOrderNumber" text null,
                "SupplierGln" text null,
                "StoreDcGln" text null,
                "IdempotencyKey" character varying(512) not null,
                "Status" character varying(64) not null,
                "SourceJson" jsonb null,
                "CanonicalJson" jsonb null,
                "ValidationJson" jsonb null,
                "CreatedAt" timestamp with time zone not null,
                "UpdatedAt" timestamp with time zone not null
            );

            create unique index "IX_invoice_candidates_IdempotencyKey"
                on invoice_candidates ("IdempotencyKey");
            """);
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

    private static InvoiceCandidateEntity NewCandidate(string key, string invoiceNumber)
        => new()
        {
            Id = Guid.NewGuid(),
            AcumaticaInvoiceId = invoiceNumber,
            InvoiceNumber = invoiceNumber,
            CustomerAccount = "SHOPRITE",
            IdempotencyKey = key,
            Status = "Candidate",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static InvoiceSubmissionAttemptEntity NewAttempt(Guid invoiceCandidateId)
        => new()
        {
            Id = Guid.NewGuid(),
            InvoiceCandidateId = invoiceCandidateId,
            InitiatedBy = "tester",
            InitiationMode = "Manual",
            Status = "Failed",
            CreatedAt = DateTimeOffset.UtcNow
        };

    private static ShopritePurchaseOrderEntity NewPurchaseOrder(string purchaseOrderNumber)
        => new()
        {
            Id = Guid.NewGuid(),
            PurchaseOrderNumber = purchaseOrderNumber,
            DeliveryLocationSource = "buyer",
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow
        };
}
