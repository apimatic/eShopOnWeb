using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription plan a shopper can enroll in. Maps to a Maxio "Product"
/// belonging to the configured product family. Handles are stable; numeric ids are not.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>The stable API handle of the plan (e.g. <c>eshop-pro</c>). Use this to subscribe.</summary>
    public string Handle { get; init; } = string.Empty;

    /// <summary>The current numeric product id in Maxio. Informational only; may change on re-seed.</summary>
    public int ProductId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price of the plan, in integer cents.</summary>
    public int PriceInCents { get; init; }

    /// <summary>The numeric interval, e.g. <c>1</c> combined with <see cref="IntervalUnit"/> of <c>month</c>.</summary>
    public int Interval { get; init; }

    /// <summary>The interval unit, e.g. <c>month</c> or <c>day</c>.</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>The handle of the product family this plan belongs to.</summary>
    public string ProductFamilyHandle { get; init; } = string.Empty;

    /// <summary>The plan price as a decimal amount (dollars), derived from <see cref="PriceInCents"/>.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>The plan price formatted for display, e.g. <c>$299.00</c>.</summary>
    public string FormattedPrice => Price.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
}
