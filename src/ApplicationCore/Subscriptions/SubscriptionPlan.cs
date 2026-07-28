namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring subscription plan a shopper can enroll in. Modeled after a Maxio
/// "product" that belongs to the configured product family. Handles are stable;
/// numeric ids are intentionally not surfaced to callers.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(
        string handle,
        string name,
        string? description,
        int priceInCents,
        int interval,
        string intervalUnit,
        bool requiresPaymentMethod,
        string productFamilyHandle)
    {
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        RequiresPaymentMethod = requiresPaymentMethod;
        ProductFamilyHandle = productFamilyHandle;
    }

    /// <summary>Stable API handle of the plan (e.g. "eshop-pro").</summary>
    public string Handle { get; }

    /// <summary>Human friendly plan name (e.g. "Pro Plan").</summary>
    public string Name { get; }

    public string? Description { get; }

    /// <summary>Recurring price in integer cents, as returned by Maxio.</summary>
    public int PriceInCents { get; }

    /// <summary>Numerical billing interval (e.g. 1).</summary>
    public int Interval { get; }

    /// <summary>Billing interval unit (e.g. "month" or "day").</summary>
    public string IntervalUnit { get; }

    /// <summary>Whether a payment profile is required to subscribe to this plan.</summary>
    public bool RequiresPaymentMethod { get; }

    /// <summary>Handle of the product family this plan belongs to.</summary>
    public string ProductFamilyHandle { get; }
}
