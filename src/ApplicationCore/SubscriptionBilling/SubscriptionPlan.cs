namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// A purchasable subscription plan (a Maxio Advanced Billing product) exposed to shoppers.
/// </summary>
public class SubscriptionPlan
{
    public int Id { get; init; }

    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>
    /// Recurring price of the plan expressed in the smallest currency unit (e.g. cents).
    /// </summary>
    public int PriceInCents { get; init; }

    /// <summary>
    /// ISO 4217 currency code used by the Maxio site (e.g. USD).
    /// </summary>
    public string Currency { get; init; } = "USD";

    /// <summary>
    /// Number of interval units between billings (e.g. 1).
    /// </summary>
    public int Interval { get; init; }

    /// <summary>
    /// Unit of the billing interval (e.g. month).
    /// </summary>
    public string IntervalUnit { get; init; } = string.Empty;
}
