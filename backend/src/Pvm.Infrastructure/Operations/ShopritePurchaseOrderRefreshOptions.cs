namespace Pvm.Infrastructure.Operations;

public sealed class ShopritePurchaseOrderRefreshOptions
{
    public const string SectionName = "ShopritePoRefresh";

    /// <summary>
    /// Whether the integration may read Shoprite orders at all. Off by default. Shoprite marks an
    /// order as downloaded the moment anything reads it, which takes the order away from the
    /// people who work it on the Shoprite portal. Turn this on only if PVM adopts Shoprite's EDI
    /// process, in which the system downloads orders and people read them under "Old".
    /// </summary>
    public bool Enabled { get; set; }

    public int ScheduleIntervalMinutes { get; set; } = 5;

    public int StaleAfterMinutes { get; set; } = 15;
}
