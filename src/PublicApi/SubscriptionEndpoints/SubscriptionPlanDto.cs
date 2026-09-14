using System.Globalization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API representation of a subscribable plan (a Maxio product in the configured family).</summary>
public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Recurring price in integer cents (lossless).</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price in the currency's major unit (e.g. dollars).</summary>
    public decimal Price { get; set; }

    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Display-friendly price, e.g. <c>$299.00/month</c>.</summary>
    public string FormattedPrice { get; set; } = string.Empty;
}
