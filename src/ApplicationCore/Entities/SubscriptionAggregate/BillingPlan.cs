namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a customer can subscribe to, as offered by the billing provider.
/// </summary>
public class BillingPlan
{
    public BillingPlan(int id, string handle, string name, string? description, int priceInCents,
        int interval, string intervalUnit, bool requiresPaymentMethod)
    {
        Id = id;
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        RequiresPaymentMethod = requiresPaymentMethod;
    }

    public int Id { get; }
    public string Handle { get; }
    public string Name { get; }
    public string? Description { get; }

    /// <summary>
    /// The recurring price in minor units (cents), exactly as the provider reports it.
    /// </summary>
    public int PriceInCents { get; }

    /// <summary>
    /// The recurring price in major units (dollars), derived from <see cref="PriceInCents"/>.
    /// </summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>
    /// How many <see cref="IntervalUnit"/>s make up one billing period (e.g. 1 month).
    /// </summary>
    public int Interval { get; }

    public string IntervalUnit { get; }
    public bool RequiresPaymentMethod { get; }
}
