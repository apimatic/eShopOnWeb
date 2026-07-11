namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The running period-to-date total for a metered component. <see cref="Available"/> is false when the
/// usage was recorded successfully but the provider read-back failed (UC2 failure scenario) - the caller
/// should still report the usage as recorded, with the total marked unavailable.
/// </summary>
public class UsagePeriodSummary
{
    public UsagePeriodSummary(string componentHandle, double? periodToDateQuantity, bool available)
    {
        ComponentHandle = componentHandle;
        PeriodToDateQuantity = periodToDateQuantity;
        Available = available;
    }

    public string ComponentHandle { get; }
    public double? PeriodToDateQuantity { get; }
    public bool Available { get; }
}
