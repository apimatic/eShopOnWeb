namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan (billing provider "product") a customer can subscribe to. Provider-agnostic
/// read model returned by <see cref="Interfaces.IBillingClient.ListPlansAsync"/>.
/// </summary>
public class BillingPlan
{
    public BillingPlan(int id, string handle, string name, long priceInCents, int interval, string intervalUnit)
    {
        Id = id;
        Handle = handle;
        Name = name;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
    }

    public int Id { get; }
    public string Handle { get; }
    public string Name { get; }
    public long PriceInCents { get; }
    public int Interval { get; }
    public string IntervalUnit { get; }
}
