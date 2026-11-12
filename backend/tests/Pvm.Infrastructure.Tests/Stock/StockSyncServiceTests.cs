using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pvm.Application.Stock;
using Pvm.Infrastructure.Stock;
using Xunit;

namespace Pvm.Infrastructure.Tests.Stock;

public sealed class StockSyncServiceTests
{
    [Fact]
    public async Task Shadow_mode_plans_every_item_and_writes_nothing()
    {
        var store = new FakeStore([Item("ENER12", 3000), Item("DYNA24", 15)]);
        var service = NewService(store, new FakeAcumatica(("ENER12", 3408m), ("DYNA24", 12m)), StockSyncMode.Shadow);

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal(2, result.Updates);
        Assert.Equal(0, result.Written);
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task Write_mode_sets_only_the_items_that_changed()
    {
        var store = new FakeStore([Item("ENER12", 3000), Item("DYNA24", 12)]);
        var service = NewService(store, new FakeAcumatica(("ENER12", 3408m), ("DYNA24", 12m)), StockSyncMode.Write);

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal(1, result.Written);
        var write = Assert.Single(store.Writes);
        Assert.Equal(("ENER12", 3408), write);
    }

    [Fact]
    public async Task Write_mode_refuses_a_run_that_would_zero_too_many_items()
    {
        var items = Enumerable.Range(1, 4).Select(i => Item($"SKU{i}", 50)).ToList();
        var figures = items.Select(i => (i.Sku, 0m)).ToArray();
        var store = new FakeStore(items);
        var service = NewService(store, new FakeAcumatica(figures), StockSyncMode.Write, maximumZeroed: 3);

        var result = await service.RunAsync(CancellationToken.None);

        Assert.NotNull(result.Refusal);
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task Disabled_mode_reads_nothing()
    {
        var store = new FakeStore([Item("ENER12", 3000)]);
        var service = NewService(store, new FakeAcumatica(("ENER12", 3408m)), StockSyncMode.Disabled);

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Empty(result.Lines);
        Assert.Equal(0, store.Reads);
    }

    private static WebStockItem Item(string sku, int current) => new(sku, 1, 2, WebStockManagement.Own, current);

    private static StockSyncService NewService(
        IWooCommerceStockClient store,
        IAcumaticaStockReader acumatica,
        StockSyncMode mode,
        int maximumZeroed = 10)
        => new(
            store,
            acumatica,
            Options.Create(new StockSyncOptions { Mode = mode, MaximumItemsZeroedPerRun = maximumZeroed }),
            NullLogger<StockSyncService>.Instance);

    private sealed class FakeStore(IReadOnlyList<WebStockItem> items) : IWooCommerceStockClient
    {
        public List<(string Sku, int Quantity)> Writes { get; } = [];

        public int Reads { get; private set; }

        public Task<IReadOnlyList<WebStockItem>> ListStockItemsAsync(CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(items);
        }

        public Task<IReadOnlyList<WebOrderLine>> ListPendingOrderLinesAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<WebOrderLine>>([]);

        public Task SetStockQuantityAsync(WebStockItem item, int quantity, CancellationToken cancellationToken)
        {
            Writes.Add((item.Sku, quantity));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAcumatica(params (string Sku, decimal OnHand)[] figures) : IAcumaticaStockReader
    {
        public Task<AcumaticaStockSnapshot> ReadAsync(
            IReadOnlyCollection<string> inventoryCds,
            IReadOnlyCollection<string> webOrderIds,
            CancellationToken cancellationToken)
            => Task.FromResult(new AcumaticaStockSnapshot(
                new HashSet<string>(figures.Select(f => f.Sku), StringComparer.OrdinalIgnoreCase),
                figures.ToDictionary(
                    f => f.Sku,
                    f => new AcumaticaStockFigures(f.Sku, f.OnHand, 0, 0, 0, 0, 0),
                    StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, string>(),
                []));
    }
}
