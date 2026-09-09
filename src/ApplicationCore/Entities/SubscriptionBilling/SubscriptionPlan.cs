namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

/// <summary>
/// A plan a shopper can subscribe to, projected from a billing-system product.
/// Identified by its stable <see cref="Handle"/> (numeric ids are reassigned on re-seed).
/// </summary>
public sealed class SubscriptionPlan
{
    public SubscriptionPlan(
        string handle,
        string name,
        string? description,
        long priceInCents,
        int interval,
        string intervalUnit,
        string productFamilyHandle)
    {
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        ProductFamilyHandle = productFamilyHandle;
    }

    /// <summary>Stable API handle, e.g. <c>eshop-pro</c>. Use this to subscribe.</summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; }

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; }

    /// <summary>Billing interval count, e.g. <c>1</c> in "1 month".</summary>
    public int Interval { get; }

    /// <summary>Billing interval unit, e.g. <c>month</c> or <c>day</c>.</summary>
    public string IntervalUnit { get; }

    public string ProductFamilyHandle { get; }
}
