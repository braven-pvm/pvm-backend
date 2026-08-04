namespace Pvm.Infrastructure.Operations;

public sealed class ShopritePurchaseOrderRefreshOptions
{
    public const string SectionName = "ShopritePoRefresh";

    public int ScheduleIntervalMinutes { get; set; } = 5;

    public int StaleAfterMinutes { get; set; } = 15;
}
