using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A Maxio subscription as seen by the signed-in eShopOnWeb user.
/// </summary>
public class SubscriptionDto
{
    public long SubscriptionId { get; set; }

    /// <summary>Subscription state in Maxio: active, trialing, past_due, canceled, expired, etc.</summary>
    public string State { get; set; } = string.Empty;

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    public long PriceInCents { get; set; }

    public decimal Price { get; set; }

    public string? Currency { get; set; }

    /// <summary>automatic or remittance.</summary>
    public string? PaymentCollectionMethod { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    /// <summary>When the current billing period ends (i.e. when the next regularly scheduled charge occurs).</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When capture of payment will next be tried.</summary>
    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public long? BalanceInCents { get; set; }

    public bool? CancelAtEndOfPeriod { get; set; }
}
