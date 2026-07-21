namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan (billing-provider "product") a customer can subscribe to.
/// </summary>
public class BillingPlan
{
    public BillingPlan(int id, string handle, string name, long priceInCents, int intervalCount, string intervalUnit, bool requiresPaymentMethod)
    {
        Id = id;
        Handle = handle;
        Name = name;
        PriceInCents = priceInCents;
        IntervalCount = intervalCount;
        IntervalUnit = intervalUnit;
        RequiresPaymentMethod = requiresPaymentMethod;
    }

    public int Id { get; }
    public string Handle { get; }
    public string Name { get; }

    /// <summary>Price in cents — the provider's own precision-safe unit. Never store money as a floating-point type.</summary>
    public long PriceInCents { get; }

    /// <summary>Convenience decimal view of <see cref="PriceInCents"/>, e.g. for display: 29900 cents => 299.00.</summary>
    public decimal Price => PriceInCents / 100m;

    public int IntervalCount { get; }
    public string IntervalUnit { get; }
    public bool RequiresPaymentMethod { get; }
}
