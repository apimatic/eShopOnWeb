namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class BillingPlan
{
    public BillingPlan(string handle, string name, long priceInCents, int interval, BillingIntervalUnit intervalUnit, bool requiresPaymentMethod)
    {
        Handle = handle;
        Name = name;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        RequiresPaymentMethod = requiresPaymentMethod;
    }

    public string Handle { get; }
    public string Name { get; }
    public long PriceInCents { get; }
    public int Interval { get; }
    public BillingIntervalUnit IntervalUnit { get; }
    public bool RequiresPaymentMethod { get; }
}
