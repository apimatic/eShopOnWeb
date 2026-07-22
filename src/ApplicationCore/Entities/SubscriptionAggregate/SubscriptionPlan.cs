namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a customer can subscribe to, as offered by the billing provider.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(int providerProductId, string handle, string name, string? description,
        int priceInCents, int interval, string intervalUnit, bool requiresPaymentMethod)
    {
        ProviderProductId = providerProductId;
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        RequiresPaymentMethod = requiresPaymentMethod;
    }

    /// <summary>The billing provider's numeric identifier. Not stable across a sandbox re-seed.</summary>
    public int ProviderProductId { get; }

    /// <summary>The durable identifier for the plan; this is what configuration refers to.</summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; }

    /// <summary>The recurring price in minor units (cents), exactly as the provider stores it.</summary>
    public int PriceInCents { get; }

    /// <summary>The recurring price in major units (dollars).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>How many <see cref="IntervalUnit"/>s make up one billing period.</summary>
    public int Interval { get; }

    /// <summary>The billing period unit, e.g. "month" or "day".</summary>
    public string IntervalUnit { get; }

    public bool RequiresPaymentMethod { get; }
}
