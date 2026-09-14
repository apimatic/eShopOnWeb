using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>
/// A shopper's subscription as returned to the API caller. Maps the fields eShopOnWeb surfaces from
/// the Maxio subscription; Maxio remains the system of record.
/// </summary>
public class SubscriptionDto
{
    public int? Id { get; set; }

    public string? ProductHandle { get; set; }

    public string? ProductName { get; set; }

    /// <summary>Subscription state, e.g. <c>active</c>, <c>trialing</c>, <c>pending</c>.</summary>
    public string? State { get; set; }

    public long? PriceInCents { get; set; }

    /// <summary>Human-readable price, e.g. <c>$299.00</c>.</summary>
    public string? FormattedPrice { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>Next assessment/billing date (Maxio <c>next_assessment_at</c>).</summary>
    public DateTimeOffset? NextBillingAt { get; set; }
}
