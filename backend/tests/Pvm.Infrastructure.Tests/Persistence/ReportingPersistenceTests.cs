using Microsoft.EntityFrameworkCore;
using Pvm.Domain.Reporting;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Persistence;

public sealed class ReportingPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Sell_in_line_round_trips_in_the_reporting_schema()
    {
        await using (var db = CreateDbContext())
        {
            await db.Database.MigrateAsync();
            db.SellInLines.Add(NewLine(SellInDocumentType.CreditMemo, "CM000001", 1, -2, -199.98m));
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext())
        {
            var line = await db.SellInLines.SingleAsync();
            Assert.Equal(SellInDocumentType.CreditMemo, line.DocumentType);
            Assert.Equal(new DateOnly(2026, 10, 27), line.DocumentDate);
            Assert.Equal(2027, line.FinancialYear);
            Assert.Equal(8, line.FinancialMonth);
            Assert.Equal(-199.98m, line.Amount);
            Assert.Equal(-2m, line.Quantity);

            var table = await db.Database
                .SqlQueryRaw<string>("SELECT table_schema AS \"Value\" FROM information_schema.tables WHERE table_name = 'sell_in_lines'")
                .SingleAsync();
            Assert.Equal("reporting", table);
        }
    }

    [Fact]
    public async Task Same_document_line_cannot_load_twice()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        db.SellInLines.Add(NewLine(SellInDocumentType.Invoice, "IN000001", 1, 5, 500m));
        db.SellInLines.Add(NewLine(SellInDocumentType.Invoice, "IN000001", 1, 5, 500m));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Invoice_and_credit_memo_can_share_a_reference_number()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        db.SellInLines.Add(NewLine(SellInDocumentType.Invoice, "000001", 1, 5, 500m));
        db.SellInLines.Add(NewLine(SellInDocumentType.CreditMemo, "000001", 1, -5, -500m));
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.SellInLines.CountAsync());
    }

    [Fact]
    public async Task Dimensions_are_keyed_by_acumatica_id()
    {
        var now = DateTimeOffset.UtcNow;
        await using (var db = CreateDbContext())
        {
            await db.Database.MigrateAsync();
            db.ReportingCustomers.Add(new ReportingCustomerEntity { CustomerId = "C0001", Name = "Example Store", UpdatedAt = now });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext())
        {
            db.ReportingCustomers.Add(new ReportingCustomerEntity { CustomerId = "C0001", Name = "Example Store 2", UpdatedAt = now });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    private PvmDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PvmDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        return new PvmDbContext(options);
    }

    private static SellInLineEntity NewLine(
        SellInDocumentType type, string reference, int lineNumber, decimal quantity, decimal amount)
    {
        var date = new DateOnly(2026, 10, 27);
        var month = FinancialMonth.FromDate(date);
        return new SellInLineEntity
        {
            Id = Guid.NewGuid(),
            DocumentType = type,
            ReferenceNumber = reference,
            LineNumber = lineNumber,
            DocumentDate = date,
            FinancialYear = month.FinancialYear,
            FinancialMonth = month.Number,
            PostPeriod = "202609",
            CustomerId = "C0001",
            SalesRepId = "REP01",
            InventoryId = "ITEM01",
            WarehouseId = "WH01",
            LocationId = "FG",
            Quantity = quantity,
            Uom = "BOX",
            BaseQuantity = quantity * 20,
            Amount = amount,
            LoadedAt = DateTimeOffset.UtcNow
        };
    }
}
