namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A subscribable plan offered to shoppers. This is a Maxio "product" within a product
/// family, projected into eShopOnWeb's own domain shape so that no billing-SDK type
/// ever leaks past the Infrastructure layer.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable, human-readable identifier (e.g. <c>eshop-pro</c>). Handles survive a re-seed; numeric ids do not.</summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; init; }

    public string Currency { get; init; } = "USD";

    /// <summary>Billing period unit as reported by Maxio, e.g. <c>month</c> or <c>day</c>.</summary>
    public string Interval { get; init; } = "month";

    /// <summary>Number of <see cref="Interval"/> units per billing period (usually 1).</summary>
    public int IntervalCount { get; init; } = 1;

    /// <summary>Handle of the product family this plan belongs to.</summary>
    public string ProductFamilyHandle { get; init; } = string.Empty;
}
