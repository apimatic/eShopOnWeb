namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>A recurring plan (Maxio "Product") a customer can subscribe to.</summary>
public class BillingPlan
{
    public BillingPlan(string handle, string name, decimal price, string billingIntervalUnit, int billingIntervalCount, bool requiresPaymentMethod)
    {
        Handle = handle;
        Name = name;
        Price = price;
        BillingIntervalUnit = billingIntervalUnit;
        BillingIntervalCount = billingIntervalCount;
        RequiresPaymentMethod = requiresPaymentMethod;
    }

    public string Handle { get; }
    public string Name { get; }
    public decimal Price { get; }

    /// <summary>e.g. "month" or "day".</summary>
    public string BillingIntervalUnit { get; }
    public int BillingIntervalCount { get; }
    public bool RequiresPaymentMethod { get; }
}
