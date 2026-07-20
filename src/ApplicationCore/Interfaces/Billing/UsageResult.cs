namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

public class UsageResult
{
    public UsageResult(UsageRecord usage, int? periodToDateBalance)
    {
        Usage = usage;
        PeriodToDateBalance = periodToDateBalance;
    }

    public UsageRecord Usage { get; }

    /// <summary>Null when the provider accepted the usage but the read-back of the running total failed.</summary>
    public int? PeriodToDateBalance { get; }
}
