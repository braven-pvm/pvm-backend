using Microsoft.EntityFrameworkCore;
using Pvm.Infrastructure.Acumatica;
using Pvm.Infrastructure.Messaging;
using Pvm.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Acumatica;

public sealed class AcumaticaPushNotificationInboxTests : IAsyncLifetime
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
    public async Task ReceiveAsync_PersistsEventAndDiscoveryMessagesAtomically()
    {
        await using var db = CreateDbContext();
        var inbox = new AcumaticaPushNotificationInbox(db, new IntegrationCommandQueue(db));
        var (notification, payload) = Notification();

        var receipt = await inbox.ReceiveAsync(notification, payload, "QA", default);

        Assert.False(receipt.Duplicate);
        Assert.Single(receipt.MessageIds);
        var stored = await db.IntegrationEventInbox.SingleAsync();
        Assert.Equal(notification.Id, stored.TransactionId);
        Assert.Equal(1, stored.EnqueuedCount);
        Assert.Equal(0, stored.DuplicateCount);
        var outbox = await db.IntegrationOutboxMessages.SingleAsync();
        Assert.Equal("acumatica.invoice-changed.v1", outbox.MessageType);
        Assert.Equal(stored.Id.ToString("D"), outbox.CausationId);
    }

    [Fact]
    public async Task ReceiveAsync_ConcurrentDuplicateCreatesOneEventAndOneMessage()
    {
        var (notification, payload) = Notification();

        await Task.WhenAll(ReceiveAsync(), ReceiveAsync());

        await using var verification = CreateDbContext();
        var stored = await verification.IntegrationEventInbox.SingleAsync();
        Assert.Equal(1, stored.DuplicateCount);
        Assert.Equal(1, await verification.IntegrationOutboxMessages.CountAsync());

        async Task ReceiveAsync()
        {
            await using var db = CreateDbContext();
            await new AcumaticaPushNotificationInbox(db, new IntegrationCommandQueue(db))
                .ReceiveAsync(notification, payload, "QA", default);
        }
    }

    private PvmDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PvmDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new PvmDbContext(options);
    }

    private static (AcumaticaPushNotification Notification, string Payload) Notification()
    {
        const string payload = """
            {
              "Inserted": [
                { "InvoiceId": "c340d968-71a3-4ced-8c91-7cf1e653bec4", "ReferenceNbr": "INV000123" }
              ],
              "Deleted": [],
              "Query": "PVM-Shoprite-Finalized-Invoices",
              "CompanyId": "PVM",
              "Id": "1af4d140-5321-41f2-a2ec-50b67f577c6c",
              "TimeStamp": 639269280000000000,
              "AdditionalInfo": {}
            }
            """;
        return (AcumaticaPushNotificationParser.Parse(payload), payload);
    }
}
