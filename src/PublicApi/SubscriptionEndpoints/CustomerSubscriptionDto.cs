using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription as recorded in Maxio, returned after subscribing and when listing.
/// </summary>
public class CustomerSubscriptionDto
{
    public long Id { get; set; }

    /// <summary>Maxio subscription state (e.g. <c>active</c>).</summary>
    public string State { get; set; } = string.Empty;

    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }

    public decimal Price { get; set; }
    public long PriceInCents { get; set; }

    public string? IntervalUnit { get; set; }
    public int IntervalCount { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    /// <summary>The next date the subscription will be billed.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>
    /// True when the subscription already existed and was returned unchanged (idempotent
    /// re-subscribe) rather than newly created by this request.
    /// </summary>
    public bool AlreadyExisted { get; set; }
}
