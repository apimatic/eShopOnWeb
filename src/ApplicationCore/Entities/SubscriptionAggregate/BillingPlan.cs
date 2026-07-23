namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a customer can subscribe to, as exposed by the billing provider.
/// Monetary values are normalized to decimal currency units (dollars), never minor units (cents).
/// </summary>
public class BillingPlan
{
    public int Id { get; init; }

    /// <summary>The durable identifier for the plan. Numeric ids are reassigned when a sandbox is re-seeded; handles are not.</summary>
    public required string Handle { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price per billing period, in decimal currency units.</summary>
    public decimal Price { get; init; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period.</summary>
    public int Interval { get; init; }

    /// <summary>The billing period unit as reported by the provider (for example "month" or "day").</summary>
    public string? IntervalUnit { get; init; }

    public string? ProductFamilyHandle { get; init; }

    /// <summary>True when the provider demands a payment method before the subscription can be created.</summary>
    public bool RequiresPaymentMethod { get; init; }

    public bool IsArchived { get; init; }
}
