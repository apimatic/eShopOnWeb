using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a customer can subscribe to, as reported by the billing provider.
/// </summary>
public sealed class BillingPlan
{
    public BillingPlan(long id,
        string handle,
        string name,
        int priceInCents,
        int interval,
        string intervalUnit,
        string productFamilyHandle,
        bool requiresPaymentMethod,
        string? description = null)
    {
        Id = Guard.Against.NegativeOrZero(id, nameof(id));
        Handle = Guard.Against.NullOrWhiteSpace(handle, nameof(handle));
        Name = Guard.Against.NullOrWhiteSpace(name, nameof(name));
        PriceInCents = Guard.Against.Negative(priceInCents, nameof(priceInCents));
        Interval = interval;
        IntervalUnit = intervalUnit;
        ProductFamilyHandle = productFamilyHandle;
        RequiresPaymentMethod = requiresPaymentMethod;
        Description = description;
    }

    /// <summary>Provider-assigned identifier. Not stable across sandbox re-seeds — resolve from <see cref="Handle"/>.</summary>
    public long Id { get; }

    /// <summary>Stable, human-authored identifier. This is what configuration refers to.</summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; }

    /// <summary>Recurring price in minor units (cents). The provider is authoritative for the currency.</summary>
    public int PriceInCents { get; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period (e.g. 1 with "month").</summary>
    public int Interval { get; }

    /// <summary>The billing period unit, e.g. "month" or "day".</summary>
    public string IntervalUnit { get; }

    public string ProductFamilyHandle { get; }

    /// <summary>True when the provider will refuse enrollment without a stored payment method.</summary>
    public bool RequiresPaymentMethod { get; }

    /// <summary>The recurring price expressed in major units (e.g. dollars).</summary>
    public decimal Price => PriceInCents / 100m;
}
