using Microsoft.EntityFrameworkCore;
using Pvm.Application.Messaging;
using Pvm.Infrastructure.Messaging;
using Pvm.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Messaging;

public sealed class IntegrationMessagingPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await DatabaseMigrationRunner.MigrateAsync(db);
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task OutboxLease_IsExclusive_AndExpiredLeaseCanBeReclaimed()
    {
        var messageId = Guid.NewGuid();
        await using (var db = CreateDbContext())
        {
            var queue = new IntegrationCommandQueue(db);
            await queue.EnqueueAsync(
                IntegrationQueues.AcumaticaInvoiceDiscovery,
                IntegrationMessageTypes.AcumaticaInvoiceDiscoveryV1,
                new DiscoverAcumaticaInvoicesMessage("test@pvm.co.za"),
                messageId.ToString("D"),
                messageId: messageId);
        }

        await using var firstDb = CreateDbContext();
        var firstLease = await new OutboxRepository(firstDb).ClaimAsync(1, TimeSpan.FromMinutes(5), default);
        Assert.Single(firstLease.Messages);

        await using (var competingDb = CreateDbContext())
        {
            var competingLease = await new OutboxRepository(competingDb).ClaimAsync(1, TimeSpan.FromMinutes(1), default);
            Assert.Empty(competingLease.Messages);
        }

        await using (var expiredDb = CreateDbContext())
        {
            await expiredDb.IntegrationOutboxMessages
                .Where(message => message.Id == messageId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(message => message.LeaseExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }
        await using var restartDb = CreateDbContext();
        var restartLease = await new OutboxRepository(restartDb).ClaimAsync(1, TimeSpan.FromMinutes(1), default);
        Assert.Equal(messageId, Assert.Single(restartLease.Messages).Id);
        Assert.Equal(2, restartLease.Messages[0].PublishAttempts);
    }

    [Fact]
    public async Task CompletedDelivery_RedeliveryKeepsSingleTerminalRecord()
    {
        var messageId = Guid.NewGuid().ToString("D");
        await using (var db = CreateDbContext())
        {
            var repository = new IntegrationDeliveryRepository(db);
            var first = await repository.RecordReceivedAsync(
                IntegrationQueues.AcumaticaInvoiceDiscovery,
                messageId,
                IntegrationMessageTypes.AcumaticaInvoiceDiscoveryV1,
                messageId,
                null,
                1,
                DateTimeOffset.UtcNow,
                default);
            await repository.MarkCompletedAsync(first.Id, default);
        }

        await using (var db = CreateDbContext())
        {
            var repository = new IntegrationDeliveryRepository(db);
            var redelivery = await repository.RecordReceivedAsync(
                IntegrationQueues.AcumaticaInvoiceDiscovery,
                messageId,
                IntegrationMessageTypes.AcumaticaInvoiceDiscoveryV1,
                messageId,
                null,
                2,
                DateTimeOffset.UtcNow,
                default);
            Assert.Equal("Completed", redelivery.Status);
            Assert.Equal(2, redelivery.DeliveryCount);
            Assert.Equal(1, await db.IntegrationMessageDeliveries.CountAsync());
        }
    }

    [Fact]
    public async Task MalformedMessage_IsPersistedAsActionableDeadLetter()
    {
        await using var db = CreateDbContext();
        var processor = new IntegrationMessageProcessor(
            new IntegrationDeliveryRepository(db),
            null!,
            null!,
            null!,
            null!);

        var result = await processor.ProcessAsync(
            IntegrationQueues.ShopriteInvoiceSubmit,
            "malformed-message-id",
            "not-json",
            1,
            5,
            DateTimeOffset.UtcNow,
            default);

        Assert.Equal(MessageSettlement.DeadLetter, result.Settlement);
        var delivery = await db.IntegrationMessageDeliveries.SingleAsync();
        Assert.Equal("DeadLettered", delivery.Status);
        Assert.Equal("invalid-json", delivery.DeadLetterReason);
        Assert.NotNull(delivery.ErrorSummary);
    }

    [Theory]
    [InlineData("request failed: password=do-not-store; retry", "do-not-store")]
    [InlineData("https://user:do-not-store@example.invalid", "do-not-store")]
    [InlineData("{\"access_token\":\"do-not-store\"}", "do-not-store")]
    public void DiagnosticSummary_RedactsCredentialShapedValues(string input, string secret)
    {
        var summary = OutboxRepository.SafeSummary(input);

        Assert.DoesNotContain(secret, summary);
        Assert.Contains("[REDACTED]", summary);
    }

    private PvmDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PvmDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new PvmDbContext(options);
    }
}
