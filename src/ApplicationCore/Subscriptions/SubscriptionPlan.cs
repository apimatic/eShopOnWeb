namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to, as published by the billing provider.
/// eShopOnWeb stores no plan of its own - the provider is the system of record.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(
        string handle,
        string name,
        string? description,
        long priceInCents,
        int interval,
        string intervalUnit,
        string? productFamilyHandle)
    {
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        ProductFamilyHandle = productFamilyHandle;
    }

    /// <summary>The provider handle - the stable identifier callers post back in order to subscribe.</summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; }

    /// <summary>The recurring price in minor units (cents), exactly as the provider reports it.</summary>
    public long PriceInCents { get; }

    /// <summary>The recurring price in major units, derived from <see cref="PriceInCents"/>.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>How many <see cref="IntervalUnit"/>s make up one billing period.</summary>
    public int Interval { get; }

    /// <summary>The provider wire value for the interval unit, e.g. month or day.</summary>
    public string IntervalUnit { get; }

    public string? ProductFamilyHandle { get; }
}
