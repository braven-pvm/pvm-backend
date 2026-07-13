using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Pvm.Infrastructure.Shoprite;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Shoprite;

public sealed class ShopriteSeedInvoiceCandidateServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task SeedAsync_PersistsReadyInvoiceCandidateFromStoredPurchaseOrder()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var order = NewPurchaseOrder();
        db.ShopritePurchaseOrders.Add(order);
        await db.SaveChangesAsync();
        var service = new ShopriteSeedInvoiceCandidateService(db);

        var candidate = await service.SeedAsync(
            order.Id,
            new DateTimeOffset(2026, 7, 8, 10, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.NotNull(candidate);
        Assert.Equal("QA-INV-1210145512", candidate.InvoiceNumber);
        Assert.Equal("SHOPRITE", candidate.CustomerAccount);
        Assert.Equal("1210145512", candidate.ShopritePurchaseOrderNumber);
        Assert.Equal(order.Id, candidate.MatchedShopritePurchaseOrderId);
        Assert.Equal("Ready", candidate.Status);

        var invoice = JsonSerializer.Deserialize<CanonicalInvoice>(
            candidate.CanonicalJson!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(invoice);
        Assert.Equal("6001197000006", invoice.SupplierGln);
        Assert.Equal("4010137059", invoice.SellerVatRegistrationNumber);
        Assert.Equal("6001002010109", invoice.StoreDcGln);
        Assert.Equal(6m, Assert.Single(invoice.Lines).Quantity);

        var validation = JsonSerializer.Deserialize<ValidationResult>(
            candidate.ValidationJson!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(validation);
        Assert.True(validation.CanSubmit);
        Assert.Contains(validation.Issues, issue => issue.Code == "unverified-shoprite-uom");
    }

    private PvmDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PvmDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        return new PvmDbContext(options);
    }

    private static ShopritePurchaseOrderEntity NewPurchaseOrder()
    {
        var order = new ShopritePurchaseOrderEntity
        {
            Id = Guid.NewGuid(),
            PurchaseOrderNumber = "1210145512",
            SupplierGln = "6001197000006",
            DeliveryGln = "6001002010109",
            DeliveryLocationCode = "36102",
            DeliveryLocationName = "DC CANELANDS",
            DeliveryLocationSource = "buyer",
            CurrencyCode = "ZAR",
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow,
            RawOrderJson = """{"purchaseOrderNumber":"1210145512"}"""
        };
        order.Lines.Add(new ShopritePurchaseOrderLineEntity
        {
            Id = Guid.NewGuid(),
            LineNumber = 1,
            Gtin = "06001197181125",
            BuyerItemId = "000123",
            BuyerItemDescription = "Test product",
            Description = "Test product",
            RequestedQuantity = 6m,
            MeasurementUnitCode = "EA",
            NetPrice = 100m,
            MonetaryAmountExcludingTaxes = 600m,
            MonetaryAmountIncludingTaxes = 690m
        });

        return order;
    }
}
