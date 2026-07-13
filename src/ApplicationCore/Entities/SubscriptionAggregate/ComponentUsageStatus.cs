using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The current period-to-date usage balance for a metered component on a subscription. // ValueObject
/// </summary>
public class ComponentUsageStatus
{
    public ComponentUsageStatus(string componentHandle, bool isMetered, decimal periodToDateUnitBalance, bool periodToDateUnavailable)
    {
        Guard.Against.NullOrEmpty(componentHandle, nameof(componentHandle));

        ComponentHandle = componentHandle;
        IsMetered = isMetered;
        PeriodToDateUnitBalance = periodToDateUnitBalance;
        PeriodToDateUnavailable = periodToDateUnavailable;
    }

    public string ComponentHandle { get; private set; }
    public bool IsMetered { get; private set; }
    public decimal PeriodToDateUnitBalance { get; private set; }

    /// <summary>
    /// True when the period-to-date balance could not be read back after an otherwise-successful
    /// usage report (UC2 failure scenario: report success, mark the total unavailable).
    /// </summary>
    public bool PeriodToDateUnavailable { get; private set; }
}
