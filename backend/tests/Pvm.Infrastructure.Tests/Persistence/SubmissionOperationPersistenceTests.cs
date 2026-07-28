using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Pvm.Application.Submissions;
using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Pvm.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Persistence;

public sealed class SubmissionOperationPersistenceTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Twenty_concurrent_commands_for_one_candidate_produce_one_external_post()
    {
        var candidateId = await ResetAndSeedCandidateAsync();
        var client = new CountingShopriteInvoiceClient(
            new ShopriteInvoiceResponse(true, 200, "accepted", IsAmbiguous: false),
            TimeSpan.FromMilliseconds(100));

        var results = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(async _ =>
            {
                await using var db = CreateDbContext();
                var handler = new SubmitShopriteInvoiceHandler(
                    new EfInvoiceCandidateRepository(db),
                    client);
                return await handler.HandleAsync(
                    new SubmitShopriteInvoiceCommand(
                        Guid.NewGuid(),
                        candidateId,
                        "worker@test",
                        "automatic"),
                    CancellationToken.None);
            }));

        Assert.Equal(1, client.SubmitCallCount);
        Assert.Single(results, result => result.Status == SubmitShopriteInvoiceStatus.Submitted);
        Assert.All(
            results,
            result => Assert.Contains(
                result.Status,
                new[]
                {
                    SubmitShopriteInvoiceStatus.Submitted,
                    SubmitShopriteInvoiceStatus.InProgress,
                    SubmitShopriteInvoiceStatus.DuplicateBlocked
                }));

        await using var verificationDb = CreateDbContext();
        var operation = Assert.Single(await verificationDb.SubmissionOperations.ToListAsync());
        Assert.Equal("Submitted", operation.Status);
        Assert.NotNull(operation.SendingStartedAt);
        Assert.NotNull(operation.CompletedAt);
        Assert.False(string.IsNullOrWhiteSpace(operation.SourceVersion));
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""{"source":"acumatica","version":1}"""),
            JsonNode.Parse(operation.FrozenSourceJson!)));
        Assert.Equal(
            "INV-1",
            JsonNode.Parse(operation.FrozenCanonicalJson)!["invoiceNumber"]!.GetValue<string>());
        Assert.Contains("invoiceMessage", operation.RequestPayload);
        Assert.Equal(64, operation.RequestPayloadHash.Length);
        Assert.Single(await verificationDb.InvoiceSubmissionAttempts.ToListAsync());
    }

    [Fact]
    public async Task Rejected_operation_blocks_redelivery_and_new_ordinary_commands()
    {
        var candidateId = await ResetAndSeedCandidateAsync();
        var command = new SubmitShopriteInvoiceCommand(
            Guid.Parse("3b621803-736d-45ac-832b-923d99a19f42"),
            candidateId,
            "operator@test",
            "manual");
        var client = new CountingShopriteInvoiceClient(
            new ShopriteInvoiceResponse(false, 417, "rejected", IsAmbiguous: false));

        await using (var firstDb = CreateDbContext())
        {
            var handler = new SubmitShopriteInvoiceHandler(
                new EfInvoiceCandidateRepository(firstDb),
                client);
            var first = await handler.HandleAsync(command, CancellationToken.None);
            Assert.Equal(SubmitShopriteInvoiceStatus.Failed, first.Status);
        }

        await using (var redeliveryDb = CreateDbContext())
        {
            var handler = new SubmitShopriteInvoiceHandler(
                new EfInvoiceCandidateRepository(redeliveryDb),
                client);
            var redelivery = await handler.HandleAsync(command, CancellationToken.None);
            Assert.Equal(SubmitShopriteInvoiceStatus.Failed, redelivery.Status);
        }

        await using (var newCommandDb = CreateDbContext())
        {
            var handler = new SubmitShopriteInvoiceHandler(
                new EfInvoiceCandidateRepository(newCommandDb),
                client);
            var newCommand = await handler.HandleAsync(
                command with { CommandId = Guid.NewGuid() },
                CancellationToken.None);
            Assert.Equal(SubmitShopriteInvoiceStatus.Failed, newCommand.Status);
        }

        Assert.Equal(1, client.SubmitCallCount);
    }

    [Fact]
    public async Task Concurrent_commands_do_not_supersede_a_fast_rejection()
    {
        var candidateId = await ResetAndSeedCandidateAsync();
        var client = new CountingShopriteInvoiceClient(
            new ShopriteInvoiceResponse(false, 417, "rejected", IsAmbiguous: false),
            TimeSpan.FromMilliseconds(50));

        var results = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(async _ =>
            {
                await using var db = CreateDbContext();
                var handler = new SubmitShopriteInvoiceHandler(
                    new EfInvoiceCandidateRepository(db),
                    client);
                return await handler.HandleAsync(
                    new SubmitShopriteInvoiceCommand(
                        Guid.NewGuid(),
                        candidateId,
                        "worker@test",
                        "automatic"),
                    CancellationToken.None);
            }));

        Assert.Equal(1, client.SubmitCallCount);
        Assert.All(
            results,
            result => Assert.Contains(
                result.Status,
                new[]
                {
                    SubmitShopriteInvoiceStatus.Failed,
                    SubmitShopriteInvoiceStatus.InProgress
                }));
    }

    [Fact]
    public async Task Stale_sending_operation_becomes_ambiguous_and_blocks_redelivery()
    {
        var candidateId = await ResetAndSeedCandidateAsync();
        var commandId = Guid.Parse("5e33ef28-d8e2-4ccf-8368-c246645f5786");
        Guid operationId;

        await using (var preparationDb = CreateDbContext())
        {
            var repository = new EfInvoiceCandidateRepository(preparationDb);
            var snapshot = Assert.IsType<InvoiceSubmissionSnapshot>(
                await repository.GetSubmissionSnapshotAsync(candidateId, CancellationToken.None));
            var operation = await repository.GetOrCreateSubmissionOperationAsync(
                new PrepareSubmissionOperation(
                    candidateId,
                    commandId,
                    "worker@test",
                    "automatic",
                    snapshot.IdempotencyKey,
                    snapshot.SourceVersion,
                    snapshot.SourceJson,
                    snapshot.CanonicalJson,
                    "<invoice />"),
                CancellationToken.None);
            operationId = operation.Id;
            Assert.True(await repository.TryStartSubmissionOperationAsync(
                operation.Id,
                DateTimeOffset.UtcNow.AddHours(-1),
                CancellationToken.None));
        }

        var client = new CountingShopriteInvoiceClient(
            new ShopriteInvoiceResponse(true, 200, "accepted", IsAmbiguous: false));
        await using (var redeliveryDb = CreateDbContext())
        {
            var handler = new SubmitShopriteInvoiceHandler(
                new EfInvoiceCandidateRepository(redeliveryDb),
                client);
            var result = await handler.HandleAsync(
                new SubmitShopriteInvoiceCommand(commandId, candidateId, "worker@test", "automatic"),
                CancellationToken.None);
            Assert.Equal(SubmitShopriteInvoiceStatus.ManualReviewRequired, result.Status);
            Assert.Equal(operationId, result.SubmissionOperationId);
        }

        Assert.Equal(0, client.SubmitCallCount);
    }

    [Fact]
    public async Task Recovery_racing_with_completion_keeps_candidate_and_operation_consistent()
    {
        var candidateId = await ResetAndSeedCandidateAsync();
        Guid operationId;

        await using (var preparationDb = CreateDbContext())
        {
            var repository = new EfInvoiceCandidateRepository(preparationDb);
            var snapshot = Assert.IsType<InvoiceSubmissionSnapshot>(
                await repository.GetSubmissionSnapshotAsync(candidateId, CancellationToken.None));
            var operation = await repository.GetOrCreateSubmissionOperationAsync(
                new PrepareSubmissionOperation(
                    candidateId,
                    Guid.NewGuid(),
                    "worker@test",
                    "automatic",
                    snapshot.IdempotencyKey,
                    snapshot.SourceVersion,
                    snapshot.SourceJson,
                    snapshot.CanonicalJson,
                    "<invoice />"),
                CancellationToken.None);
            operationId = operation.Id;
            Assert.True(await repository.TryStartSubmissionOperationAsync(
                operation.Id,
                DateTimeOffset.UtcNow.AddHours(-1),
                CancellationToken.None));
        }

        var completion = Task.Run(async () =>
        {
            await using var db = CreateDbContext();
            await new EfInvoiceCandidateRepository(db).CompleteSubmissionOperationAsync(
                operationId,
                new ShopriteInvoiceResponse(true, 200, "accepted", IsAmbiguous: false),
                CancellationToken.None);
        });
        var recovery = Task.Run(async () =>
        {
            await using var db = CreateDbContext();
            await new EfInvoiceCandidateRepository(db).MarkStaleSendingOperationsAmbiguousAsync(
                DateTimeOffset.UtcNow.AddMinutes(-15),
                DateTimeOffset.UtcNow,
                CancellationToken.None);
        });

        await Task.WhenAll(completion, recovery);

        await using var verificationDb = CreateDbContext();
        var operationStatus = await verificationDb.SubmissionOperations
            .Where(operation => operation.Id == operationId)
            .Select(operation => operation.Status)
            .SingleAsync();
        var candidateStatus = await verificationDb.InvoiceCandidates
            .Where(candidate => candidate.Id == candidateId)
            .Select(candidate => candidate.Status)
            .SingleAsync();
        Assert.Contains(operationStatus, new[] { "Submitted", "Ambiguous" });
        Assert.Equal(operationStatus, candidateStatus);
    }

    private async Task<Guid> ResetAndSeedCandidateAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();

        var purchaseOrder = new ShopritePurchaseOrderEntity
        {
            Id = Guid.NewGuid(),
            PurchaseOrderNumber = "PO-123",
            DeliveryLocationSource = "buyer",
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow
        };
        var invoice = ValidInvoice();
        var candidate = new InvoiceCandidateEntity
        {
            Id = Guid.NewGuid(),
            AcumaticaInvoiceId = invoice.AcumaticaInvoiceId,
            InvoiceNumber = invoice.InvoiceNumber,
            CustomerAccount = invoice.CustomerAccount,
            ShopritePurchaseOrderNumber = purchaseOrder.PurchaseOrderNumber,
            MatchedShopritePurchaseOrderId = purchaseOrder.Id,
            IdempotencyKey = $"invoice:{invoice.InvoiceNumber}",
            Status = "Ready",
            SourceJson = """{"source":"acumatica","version":1}""",
            CanonicalJson = JsonSerializer.Serialize(invoice, SerializerOptions),
            ValidationJson = JsonSerializer.Serialize(new ValidationResult([]), SerializerOptions),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        db.ShopritePurchaseOrders.Add(purchaseOrder);
        db.InvoiceCandidates.Add(candidate);
        await db.SaveChangesAsync();
        return candidate.Id;
    }

    private PvmDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PvmDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new PvmDbContext(options);
    }

    private static CanonicalInvoice ValidInvoice()
        => new(
            AcumaticaInvoiceId: "ACU-INV-1",
            InvoiceNumber: "INV-1",
            CustomerAccount: "SHOPRITE",
            CustomerLocation: "DC-01",
            ShopritePurchaseOrderNumber: "PO-123",
            SupplierGln: "6001197000006",
            SellerVatRegistrationNumber: "4010137059",
            StoreDcGln: "6001001018104",
            CountryCode: "ZA",
            CurrencyCode: "ZAR",
            InvoiceDate: new DateTimeOffset(2026, 7, 28, 10, 0, 0, TimeSpan.Zero),
            TotalExcludingTax: new Money("ZAR", 100m),
            TotalIncludingTax: new Money("ZAR", 115m),
            TotalTax: new Money("ZAR", 15m),
            Lines:
            [
                new CanonicalInvoiceLine(
                    1,
                    "SKU-1",
                    "06001197181125",
                    "Test item",
                    1m,
                    "EA",
                    ShopriteMeasurementUnit.EA,
                    1m,
                    new Money("ZAR", 100m),
                    new Money("ZAR", 115m),
                    new Money("ZAR", 15m),
                    "STANDARD",
                    15m,
                    false)
            ]);

    private sealed class CountingShopriteInvoiceClient(
        ShopriteInvoiceResponse response,
        TimeSpan? delay = null) : IShopriteInvoiceClient
    {
        private int _submitCallCount;

        public int SubmitCallCount => _submitCallCount;

        public async Task<ShopriteInvoiceResponse> SubmitAsync(
            string xml,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _submitCallCount);
            if (delay is not null)
            {
                await Task.Delay(delay.Value, cancellationToken);
            }

            return response;
        }
    }
}
