using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscribable plan, projected from a Maxio product within the configured
/// product family. Identified by its stable <see cref="Handle"/>.
/// </summary>
public class SubscriptionPlan
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Recurring price in cents (Maxio's canonical money representation).</summary>
    public int PriceInCents { get; set; }

    public string Currency { get; set; } = "USD";

    /// <summary>Billing interval count, e.g. 1.</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit, e.g. "month".</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Human-friendly price, e.g. "$299.00".</summary>
    public string FormattedPrice =>
        (PriceInCents / 100m).ToString("C2", CultureInfo.GetCultureInfo("en-US"));
}
