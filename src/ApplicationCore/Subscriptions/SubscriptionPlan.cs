namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A billing plan a shopper can subscribe to, projected from the billing system of record
/// (Maxio Advanced Billing). Identified by its stable <see cref="Handle"/> rather than a
/// numeric id, which the billing system may reassign.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable, human-readable identifier used when subscribing (e.g. "eshop-pro").</summary>
    public string Handle { get; init; } = string.Empty;

    /// <summary>Display name of the plan (e.g. "Pro Plan").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>The recurring price formatted for display (e.g. "$299.00").</summary>
    public string FormattedPrice { get; init; } = string.Empty;

    /// <summary>Human-readable billing interval (e.g. "1 month").</summary>
    public string Interval { get; init; } = string.Empty;

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string ProductFamilyHandle { get; init; } = string.Empty;

    /// <summary>Optional marketing description.</summary>
    public string? Description { get; init; }
}
