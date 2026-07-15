namespace Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;

public class SubscriptionPlan
{
    public SubscriptionPlan(string handle, string name, long priceInCents, int billingIntervalCount,
        string billingIntervalUnit, bool requiresPaymentMethod)
    {
        Handle = handle;
        Name = name;
        PriceInCents = priceInCents;
        BillingIntervalCount = billingIntervalCount;
        BillingIntervalUnit = billingIntervalUnit;
        RequiresPaymentMethod = requiresPaymentMethod;
    }

    public string Handle { get; }
    public string Name { get; }
    public long PriceInCents { get; }
    public int BillingIntervalCount { get; }
    public string BillingIntervalUnit { get; }
    public bool RequiresPaymentMethod { get; }
}
