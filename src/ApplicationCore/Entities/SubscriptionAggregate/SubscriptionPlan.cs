namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring-billing plan the shop offers, sourced from the billing system of record (Maxio Advanced Billing).
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(int maxioProductId, string handle, string name, string? description,
        long priceInCents, int interval, string intervalUnit, string productFamilyHandle)
    {
        MaxioProductId = maxioProductId;
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        ProductFamilyHandle = productFamilyHandle;
    }

    /// <summary>
    /// The product id in Maxio Advanced Billing. Numeric ids can be reassigned when a Maxio site is re-seeded;
    /// <see cref="Handle"/> is the stable identifier.
    /// </summary>
    public int MaxioProductId { get; }

    /// <summary>
    /// The stable API handle of the product in Maxio Advanced Billing.
    /// </summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; }

    public long PriceInCents { get; }

    /// <summary>
    /// The number of <see cref="IntervalUnit"/> between billings (e.g. 1 month).
    /// </summary>
    public int Interval { get; }

    public string IntervalUnit { get; }

    public string ProductFamilyHandle { get; }
}
