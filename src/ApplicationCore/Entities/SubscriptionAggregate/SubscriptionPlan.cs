using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a customer can subscribe to. The handle is the durable identifier; the numeric
/// id is assigned by the billing provider and is reassigned when the catalog is re-seeded.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(int id, string handle, string name, string? description, int priceInCents, int interval, string intervalUnit)
    {
        // The handle may be blank: Maxio allows products without one, and such a plan simply cannot
        // be the target of a subscribe or a plan change, both of which address plans by handle.
        Guard.Against.Null(handle, nameof(handle));
        Guard.Against.NullOrEmpty(name, nameof(name));

        Id = id;
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
    }

    public int Id { get; }
    public string Handle { get; }
    public string Name { get; }
    public string? Description { get; }

    /// <summary>The recurring price in minor units, as the billing provider reports it.</summary>
    public int PriceInCents { get; }

    /// <summary>The recurring price in major units (e.g. 29900 cents becomes 299.00).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>How many <see cref="IntervalUnit"/>s make up one billing period.</summary>
    public int Interval { get; }

    /// <summary>The billing period unit reported by the provider, e.g. "month" or "day".</summary>
    public string IntervalUnit { get; }

    /// <summary>A display form of the billing period, e.g. "month" or "3 months".</summary>
    public string BillingPeriod => Interval == 1 ? IntervalUnit : $"{Interval} {IntervalUnit}s";
}
