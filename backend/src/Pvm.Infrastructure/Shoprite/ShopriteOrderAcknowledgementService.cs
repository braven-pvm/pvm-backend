using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pvm.Application.Shoprite;
using Pvm.Infrastructure.Persistence;

namespace Pvm.Infrastructure.Shoprite;

public sealed record ShopriteAcknowledgementResult(
    bool Enabled,
    int Acknowledged,
    int Pending,
    string? Error = null);

/// <summary>
/// Acknowledges Shoprite orders after they are stored locally. Shoprite keeps returning the
/// same orders, and stops providing new ones, until acknowledgement succeeds.
/// </summary>
public sealed class ShopriteOrderAcknowledgementService(
    PvmDbContext dbContext,
    IShopritePurchaseOrderClient purchaseOrderClient,
    IOptions<ShopriteOptions> options)
{
    private const int BatchSize = 200;
    private readonly ShopriteOptions _options = options.Value;

    public async Task<ShopriteAcknowledgementResult> AcknowledgeStoredOrdersAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!_options.AcknowledgeOrders)
        {
            var waiting = await dbContext.ShopritePurchaseOrders
                .CountAsync(order => order.AcknowledgedAt == null, cancellationToken);
            return new ShopriteAcknowledgementResult(false, 0, waiting);
        }

        var pending = await dbContext.ShopritePurchaseOrders
            .Where(order => order.AcknowledgedAt == null)
            .OrderBy(order => order.FirstSeenAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
        {
            return new ShopriteAcknowledgementResult(true, 0, 0);
        }

        var orderNumbers = pending
            .Select(order => order.PurchaseOrderNumber)
            .ToArray();

        try
        {
            await purchaseOrderClient.AcknowledgeAsync(orderNumbers, cancellationToken);
        }
        catch (Exception exception)
        {
            foreach (var order in pending)
            {
                order.AcknowledgementAttempts++;
                order.LastAcknowledgementError = Trim(exception.Message);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            var stillPending = await dbContext.ShopritePurchaseOrders
                .CountAsync(order => order.AcknowledgedAt == null, cancellationToken);
            return new ShopriteAcknowledgementResult(true, 0, stillPending, Trim(exception.Message));
        }

        foreach (var order in pending)
        {
            order.AcknowledgedAt = now;
            order.AcknowledgementAttempts++;
            order.LastAcknowledgementError = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        var remaining = await dbContext.ShopritePurchaseOrders
            .CountAsync(order => order.AcknowledgedAt == null, cancellationToken);
        return new ShopriteAcknowledgementResult(true, pending.Count, remaining);
    }

    public async Task<int> ResetAsync(
        IReadOnlyCollection<string> purchaseOrderNumbers,
        CancellationToken cancellationToken)
    {
        if (purchaseOrderNumbers.Count == 0)
        {
            return 0;
        }

        await purchaseOrderClient.ResetAsync(purchaseOrderNumbers, cancellationToken);

        var numbers = purchaseOrderNumbers.ToArray();
        var orders = await dbContext.ShopritePurchaseOrders
            .Where(order => numbers.Contains(order.PurchaseOrderNumber))
            .ToListAsync(cancellationToken);
        foreach (var order in orders)
        {
            order.AcknowledgedAt = null;
            order.LastAcknowledgementError = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return orders.Count;
    }

    private static string Trim(string value)
        => value.Length <= 1000 ? value : value[..1000];
}
