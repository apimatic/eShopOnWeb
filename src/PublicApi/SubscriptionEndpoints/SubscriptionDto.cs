using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A shopper's subscription as recorded by Maxio Advanced Billing.</summary>
public class SubscriptionDto
{
    /// <summary>The Maxio subscription id.</summary>
    public long Id { get; set; }

    /// <summary>Subscription state (e.g. "active").</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Subscription currency (e.g. "USD").</summary>
    public string Currency { get; set; } = string.Empty;

    public long PlanId { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>Plan price in the site currency (e.g. 299.00).</summary>
    public decimal PlanPrice { get; set; }

    /// <summary>Billing interval length (e.g. 1).</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit (day, month, year...).</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>When the current billing period started.</summary>
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    /// <summary>When the subscription is next billed.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    /// <summary>When the subscription was created.</summary>
    public DateTimeOffset? CreatedAt { get; set; }
}
