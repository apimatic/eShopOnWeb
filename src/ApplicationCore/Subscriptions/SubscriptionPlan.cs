using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A billing plan a shopper can subscribe to. Projected from a Maxio "product"
/// belonging to the configured product family. This is the application-facing
/// model; it is intentionally decoupled from the Maxio wire contract.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Maxio product id. Not stable across re-seeds; prefer <see cref="Handle"/>.</summary>
    public int ProductId { get; init; }

    /// <summary>Stable API handle used to subscribe (e.g. "eshop-pro").</summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price in integer cents, as Maxio stores it.</summary>
    public long PriceInCents { get; init; }

    /// <summary>Numeric billing interval (e.g. 1).</summary>
    public int Interval { get; init; }

    /// <summary>Interval unit (e.g. "month" or "day").</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    public string ProductFamilyHandle { get; init; } = string.Empty;

    /// <summary>Human-readable price such as "$299.00".</summary>
    public string FormattedPrice =>
        (PriceInCents / 100m).ToString("C2", CultureInfo.GetCultureInfo("en-US"));
}
