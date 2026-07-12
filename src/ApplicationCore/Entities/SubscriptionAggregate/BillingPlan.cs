namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan (billing-provider "product") a customer can subscribe to.
/// </summary>
public class BillingPlan
{
    public int ProductId { get; }
    public string Handle { get; }
    public string Name { get; }
    public int PriceInCents { get; }
    public string IntervalUnit { get; }
    public bool RequiresPaymentMethod { get; }

    public BillingPlan(
        int productId,
        string handle,
        string name,
        int priceInCents,
        string intervalUnit,
        bool requiresPaymentMethod)
    {
        ProductId = productId;
        Handle = handle;
        Name = name;
        PriceInCents = priceInCents;
        IntervalUnit = intervalUnit;
        RequiresPaymentMethod = requiresPaymentMethod;
    }
}
