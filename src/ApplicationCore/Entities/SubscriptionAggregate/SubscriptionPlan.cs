using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a customer may subscribe to, as offered by the billing provider.
/// <para>
/// <see cref="Price"/> is expressed in whole currency units (for example 299.00 for $299.00). The
/// billing provider reports plan prices in integer cents; the conversion happens once, at the
/// provider seam, so no part of the application has to reason about cents.
/// </para>
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(int id,
        string handle,
        string name,
        decimal price,
        int interval,
        BillingIntervalUnit intervalUnit,
        string? description = null)
    {
        Guard.Against.NullOrEmpty(handle, nameof(handle));
        Guard.Against.NullOrEmpty(name, nameof(name));
        Guard.Against.Negative(price, nameof(price));

        Id = id;
        Handle = handle;
        Name = name;
        Price = price;
        Interval = interval;
        IntervalUnit = intervalUnit;
        Description = description;
    }

    /// <summary>The provider-assigned numeric identifier. Reassigned whenever the catalog is re-seeded.</summary>
    public int Id { get; }

    /// <summary>The stable, human-authored identifier. This is what configuration refers to.</summary>
    public string Handle { get; }

    public string Name { get; }

    /// <summary>The recurring price in whole currency units (dollars), never cents.</summary>
    public decimal Price { get; }

    /// <summary>How many <see cref="IntervalUnit"/>s make up one billing period.</summary>
    public int Interval { get; }

    public BillingIntervalUnit IntervalUnit { get; }

    public string? Description { get; }

    /// <summary>A display string for the billing period, e.g. "month" or "3 months".</summary>
    public string BillingPeriod
    {
        get
        {
            var unit = IntervalUnit == BillingIntervalUnit.Day ? "day" : "month";
            return Interval <= 1 ? unit : $"{Interval} {unit}s";
        }
    }
}
