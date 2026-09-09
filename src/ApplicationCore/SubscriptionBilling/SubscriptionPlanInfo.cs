namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// A subscribable plan (a Maxio/Billing API product) offered to shoppers.
/// </summary>
public sealed record SubscriptionPlanInfo
{
    /// <summary>
    /// The stable API handle of the plan in the billing system of record.
    /// Numeric IDs are not stable across catalog re-seeds; handles are.
    /// </summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price of the plan, in integer cents.</summary>
    public long PriceCents { get; init; }

    /// <summary>
    /// Human-readable recurring billing cadence, e.g. "1 month".
    /// </summary>
    public string BillingInterval { get; init; } = string.Empty;

    /// <summary>
    /// True when the billing system requires a payment method to complete enrollment.
    /// </summary>
    public bool RequiresPaymentMethod { get; init; }
}
