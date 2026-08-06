namespace Pvm.Infrastructure.Acumatica;

public sealed record AcumaticaReconciliationOptions
{
    public const string SectionName = "AcumaticaReconciliation";

    public int ScheduleIntervalMinutes { get; init; } = 10;

    public int OverlapMinutes { get; init; } = 15;

    public int DailyLookbackDays { get; init; } = 7;

    public int StaleAfterMinutes { get; init; } = 30;
}
