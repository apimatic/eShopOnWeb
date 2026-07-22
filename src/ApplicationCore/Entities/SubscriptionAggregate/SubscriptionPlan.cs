namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a customer can subscribe to, as exposed by the billing provider.
/// </summary>
/// <remarks>
/// <see cref="Handle"/> is the durable identifier: the provider assigns <see cref="Id"/> and
/// reassigns it whenever the catalog is re-created, so configuration and code key off the handle.
/// <see cref="Price"/> is expressed in whole currency units (dollars), never in cents — the
/// billing client converts the provider's minor-unit amounts on the way in.
/// </remarks>
public sealed record SubscriptionPlan
{
    public required int Id { get; init; }

    public required string Handle { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>The recurring price in whole currency units (for example 299.00, not 29900).</summary>
    public required decimal Price { get; init; }

    /// <summary>How many <see cref="IntervalUnit"/>s make up one billing period.</summary>
    public required int Interval { get; init; }

    /// <summary>The billing period unit, for example "month" or "day".</summary>
    public required string IntervalUnit { get; init; }

    public string? ProductFamilyHandle { get; init; }

    /// <summary>True when the provider demands a payment method before enrollment succeeds.</summary>
    public bool RequiresPaymentMethod { get; init; }

    public bool IsArchived { get; init; }

    /// <summary>A human-readable rendering of the billing cadence, for example "month" or "3 months".</summary>
    public string BillingPeriodDescription =>
        Interval == 1 ? IntervalUnit : $"{Interval} {IntervalUnit}s";
}
