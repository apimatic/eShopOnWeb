using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a customer can subscribe to, normalized from the billing provider.
/// </summary>
/// <remarks>
/// <see cref="Price"/> is always expressed in whole currency units (dollars), never in
/// minor units (cents). Providers that report money in cents are converted at the seam.
/// </remarks>
public class BillingPlan
{
    public BillingPlan(int id,
        string handle,
        string name,
        decimal price,
        int intervalLength,
        string intervalUnit)
    {
        Guard.Against.NullOrEmpty(handle, nameof(handle));
        Guard.Against.NullOrEmpty(name, nameof(name));
        Guard.Against.Negative(price, nameof(price));
        Guard.Against.NullOrEmpty(intervalUnit, nameof(intervalUnit));

        Id = id;
        Handle = handle;
        Name = name;
        Price = price;
        IntervalLength = intervalLength;
        IntervalUnit = intervalUnit;
    }

    /// <summary>The provider-assigned identifier. Not stable across a sandbox re-seed.</summary>
    public int Id { get; }

    /// <summary>The stable identifier this integration is configured against.</summary>
    public string Handle { get; }

    public string Name { get; }

    /// <summary>Recurring price in whole currency units (dollars).</summary>
    public decimal Price { get; }

    /// <summary>How many <see cref="IntervalUnit"/>s each billing period spans.</summary>
    public int IntervalLength { get; }

    /// <summary>The billing period unit as reported by the provider (e.g. "month", "day").</summary>
    public string IntervalUnit { get; }

    public string? Description { get; init; }

    /// <summary>True when the provider requires a payment method before enrollment can succeed.</summary>
    public bool RequiresPaymentMethod { get; init; }

    /// <summary>True when the plan has been archived and can no longer be subscribed to.</summary>
    public bool IsArchived { get; init; }

    /// <summary>The handle of the product family the plan belongs to, when the provider reports one.</summary>
    public string? ProductFamilyHandle { get; init; }

    /// <summary>A short human-readable rendering of the recurring cost, e.g. "$299.00 / month".</summary>
    public string PriceDescription => IntervalLength == 1
        ? $"${Price:N2} / {IntervalUnit}"
        : $"${Price:N2} / {IntervalLength} {IntervalUnit}s";
}
