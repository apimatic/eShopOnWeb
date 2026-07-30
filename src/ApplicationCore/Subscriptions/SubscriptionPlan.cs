namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring subscription plan a shopper can enroll in. This is a billing-system-agnostic
/// projection of a Maxio Advanced Billing "product" within the configured product family.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(string handle, string name, string? description,
        long priceInCents, int interval, string intervalUnit, string productFamilyHandle,
        bool requiresPaymentMethod)
    {
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        ProductFamilyHandle = productFamilyHandle;
        RequiresPaymentMethod = requiresPaymentMethod;
    }

    /// <summary>Stable API handle of the plan (e.g. <c>eshop-pro</c>). Safe to persist/reference.</summary>
    public string Handle { get; }

    /// <summary>Human-friendly plan name (e.g. "Pro Plan").</summary>
    public string Name { get; }

    public string? Description { get; }

    /// <summary>Recurring price in integer cents.</summary>
    public long PriceInCents { get; }

    /// <summary>Recurring price as a decimal amount (cents / 100).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>The numeric billing interval (e.g. 1).</summary>
    public int Interval { get; }

    /// <summary>The billing interval unit (e.g. "month" or "day").</summary>
    public string IntervalUnit { get; }

    /// <summary>Handle of the product family this plan belongs to.</summary>
    public string ProductFamilyHandle { get; }

    /// <summary>Whether a payment method must be captured before subscribing.</summary>
    public bool RequiresPaymentMethod { get; }
}
