namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A recurring subscription plan offered by the billing provider for a product family.
/// This is the billing provider's "product" surfaced through its default price point.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>
    /// Numeric id assigned by the billing provider. Not stable across re-seeds; use <see cref="Handle"/>.
    /// </summary>
    public int Id { get; init; }

    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Recurring price in the site's currency (major units, e.g. 299.00).
    /// </summary>
    public decimal Price { get; init; }

    public string Currency { get; init; } = string.Empty;

    /// <summary>
    /// Billing frequency (e.g. 1).
    /// </summary>
    public int Interval { get; init; }

    /// <summary>
    /// Billing frequency unit (e.g. "month").
    /// </summary>
    public string IntervalUnit { get; init; } = "month";

    /// <summary>
    /// True when the plan has been archived by the billing provider.
    /// </summary>
    public bool IsArchived { get; init; }
}
