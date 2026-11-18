using Microsoft.EntityFrameworkCore;
using Pvm.Application.Documents;
using Pvm.Infrastructure.Documents;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Documents;

/// <summary>The search by invoice number on Postgres, where the extraction is jsonb.</summary>
public sealed class DocumentSearchTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await DatabaseMigrationRunner.MigrateAsync(db);
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Theory]
    [InlineData("INV190931")]
    [InlineData("inv190931")]
    [InlineData(" INV190931 ")]
    public async Task ByInvoice_FindsAReviewDocumentByTheInvoiceNumberReadFromThePaper(string invoice)
    {
        var onReview = await SeedAsync(
            DocumentState.NeedsReview,
            extractionJson: """
                {"InvoiceNumbers":["INV190931"],"SalesOrderNumbers":[],"CustomerOrderNumbers":["2297741"],
                 "ClientName":"Dis-Chem Mondeor S007","DeliveryDate":"2026-10-07","SignaturePresent":true,"Note":null,
                 "candidates":[{"referenceNumber":"INV190931","customerName":"DIS-CHEM PHARMACIES LTD MONDEOR S007"}]}
                """);
        await SeedAsync(
            DocumentState.NeedsReview,
            extractionJson: """{"InvoiceNumbers":["INV190932"],"ClientName":"Dis-Chem Sandton","candidates":[]}""");
        await SeedAsync(DocumentState.Filed, matchedReference: "INV190932");

        var found = await SearchAsync(invoice);

        Assert.Equal([onReview], found);
    }

    [Fact]
    public async Task ByInvoice_FindsFiledByMatchedReference_AndReviewByCandidate()
    {
        var filed = await SeedAsync(DocumentState.Filed, matchedReference: "INV500", uploadedAt: DateTimeOffset.UtcNow.AddHours(-2));
        var byCandidate = await SeedAsync(
            DocumentState.NeedsReview,
            extractionJson: """{"InvoiceNumbers":[],"CustomerOrderNumbers":["PO1"],"candidates":[{"referenceNumber":"inv500"},{"referenceNumber":"INV501"}]}""",
            uploadedAt: DateTimeOffset.UtcNow.AddHours(-1));
        await SeedAsync(DocumentState.NeedsReview, extractionJson: """{"candidates":[{"referenceNumber":"INV501"}]}""");

        var found = await SearchAsync("INV500");

        Assert.Equal([byCandidate, filed], found);
    }

    [Fact]
    public async Task ByInvoice_ANumberOnlyInANoteOrName_OrAnOddExtraction_IsNotFound()
    {
        await SeedAsync(
            DocumentState.NeedsReview,
            extractionJson: """{"InvoiceNumbers":["INV777"],"ClientName":"INV600","Note":"INV600","candidates":[]}""");
        await SeedAsync(DocumentState.NeedsReview, extractionJson: """{"unreadable":"no-text"}""");
        await SeedAsync(DocumentState.NeedsReview, extractionJson: """{"InvoiceNumbers":"INV600","candidates":{"referenceNumber":"INV600"}}""");
        await SeedAsync(DocumentState.NeedsReview, extractionJson: """[1, "INV600"]""");
        await SeedAsync(DocumentState.Received);

        Assert.Empty(await SearchAsync("INV600"));
    }

    [Fact]
    public async Task ByInvoice_ComposesWithOtherFiltersAndCount()
    {
        await SeedAsync(DocumentState.Filed, matchedReference: "INV800");
        var onReview = await SeedAsync(DocumentState.NeedsReview, extractionJson: """{"InvoiceNumbers":["INV800"]}""");

        await using var db = CreateDbContext();
        var query = DocumentSearch.ByInvoice(db, "INV800").AsNoTracking().Where(item => item.State == DocumentState.NeedsReview);

        Assert.Equal(1, await query.CountAsync());
        Assert.Equal(onReview, (await query.SingleAsync()).Id);
    }

    private async Task<List<Guid>> SearchAsync(string invoice)
    {
        await using var db = CreateDbContext();
        return await DocumentSearch.ByInvoice(db, invoice)
            .AsNoTracking()
            .OrderByDescending(item => item.UploadedAt)
            .Select(item => item.Id)
            .ToListAsync();
    }

    private async Task<Guid> SeedAsync(
        DocumentState state,
        string? matchedReference = null,
        string? extractionJson = null,
        DateTimeOffset? uploadedAt = null)
    {
        var id = Guid.NewGuid();
        var at = uploadedAt ?? DateTimeOffset.UtcNow;
        await using var db = CreateDbContext();
        db.CapturedDocuments.Add(new CapturedDocumentEntity
        {
            Id = id,
            ClientDocumentId = Guid.NewGuid(),
            DocumentType = "POD",
            State = state,
            ReviewReason = state == DocumentState.NeedsReview ? ReviewReason.ClientNameConflict : null,
            BlobPath = $"documents/POD/{at:yyyy}/{at:MM}/{id:D}.pdf",
            PageCount = 1,
            SizeBytes = 10,
            Sha256 = new string('a', 64),
            MatchedReference = matchedReference,
            ExtractionJson = extractionJson,
            UploadedBy = "driver@pvm.co.za",
            UploadedAt = at,
            UpdatedAt = at,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private PvmDbContext CreateDbContext()
        => new(new DbContextOptionsBuilder<PvmDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);
}
