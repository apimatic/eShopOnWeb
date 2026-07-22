namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The unit a plan's billing interval is counted in. The billing provider models exactly these two.
/// </summary>
public enum BillingIntervalUnit
{
    Day = 0,
    Month = 1
}
