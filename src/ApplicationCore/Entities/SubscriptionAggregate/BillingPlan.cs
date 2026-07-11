namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a customer can subscribe to, as read from the billing provider's product catalog.
/// </summary>
public class BillingPlan
{
    public BillingPlan(int productId, string handle, string name, long priceInCents, string intervalUnit, int interval)
    {
        ProductId = productId;
        Handle = handle;
        Name = name;
        PriceInCents = priceInCents;
        IntervalUnit = intervalUnit;
        Interval = interval;
    }

    public int ProductId { get; }
    public string Handle { get; }
    public string Name { get; }
    public long PriceInCents { get; }
    public string IntervalUnit { get; }
    public int Interval { get; }
}
