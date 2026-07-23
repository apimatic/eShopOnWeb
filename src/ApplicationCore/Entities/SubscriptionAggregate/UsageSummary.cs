namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The running period-to-date balance of a metered component on a subscription (UC2, step 3).
/// These units bill on the next renewal invoice.
/// </summary>
public class UsageSummary
{
    public UsageSummary(int subscriptionId,
        int componentId,
        string componentHandle,
        string componentName,
        decimal unitBalance)
    {
        SubscriptionId = subscriptionId;
        ComponentId = componentId;
        ComponentHandle = componentHandle;
        ComponentName = componentName;
        UnitBalance = unitBalance;
    }

    public int SubscriptionId { get; private set; }
    public int ComponentId { get; private set; }
    public string ComponentHandle { get; private set; }
    public string ComponentName { get; private set; }

    /// <summary>Accumulated units reported this period; the provider floors this at zero.</summary>
    public decimal UnitBalance { get; private set; }
}
