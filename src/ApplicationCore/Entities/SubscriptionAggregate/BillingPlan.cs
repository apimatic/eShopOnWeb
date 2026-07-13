namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a customer can subscribe to, as read from the billing provider.
/// </summary>
public class BillingPlan
{
    public BillingPlan(long id, string handle, string name, long priceInCents, bool requiresPaymentMethod)
    {
        Id = id;
        Handle = handle;
        Name = name;
        PriceInCents = priceInCents;
        RequiresPaymentMethod = requiresPaymentMethod;
    }

    public long Id { get; }
    public string Handle { get; }
    public string Name { get; }
    public long PriceInCents { get; }
    public bool RequiresPaymentMethod { get; }
}
