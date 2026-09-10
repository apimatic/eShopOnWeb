namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription plan a shopper can enroll in, projected from the billing
/// system of record (Maxio Advanced Billing). Amounts are represented in the
/// smallest currency unit (cents) to avoid rounding, mirroring the billing API.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable, human-readable identifier for the plan (e.g. "eshop-pro"). IDs are not stable; handles are.</summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price in cents.</summary>
    public int PriceInCents { get; init; }

    /// <summary>Number of interval units per billing period (e.g. 1).</summary>
    public int Interval { get; init; }

    /// <summary>Billing interval unit as reported by the billing system (e.g. "month").</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>Whether the plan requires a payment method (card) to subscribe.</summary>
    public bool RequiresPaymentMethod { get; init; }
}
