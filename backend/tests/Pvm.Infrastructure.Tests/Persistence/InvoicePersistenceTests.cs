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

    private PvmDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PvmDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        return new PvmDbContext(options);
    }

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
}
