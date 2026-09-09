using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription held by the authenticated user, as returned by GET /api/my-subscriptions
/// and POST /api/subscriptions.
/// </summary>
public class MySubscriptionDto
{
    public int Id { get; set; }

    /// <summary>Lifecycle state, e.g. "active", "trialing", "canceled".</summary>
    public string State { get; set; } = string.Empty;

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    public long PriceInCents { get; set; }

    public decimal Price { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the next billing/charge attempt is scheduled.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}
