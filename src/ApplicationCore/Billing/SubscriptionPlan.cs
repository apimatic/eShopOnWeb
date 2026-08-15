namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A recurring-subscription plan a shopper can subscribe to. This is a
/// billing-system-agnostic projection of a Maxio product; the concrete
/// mapping from the Maxio SDK lives in the Infrastructure layer.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable, human-readable identifier of the plan (Maxio product handle).</summary>
    public string Handle { get; init; } = string.Empty;

    /// <summary>Billing-system identifier of the plan (Maxio product id). Not stable across re-seeds.</summary>
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring price as a decimal amount (e.g. 299.00).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>Number of interval units between charges (e.g. 1).</summary>
    public int Interval { get; init; }

    /// <summary>Interval unit for the recurring charge (e.g. "month").</summary>
    public string? IntervalUnit { get; init; }

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; init; }
}
