using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>Maxio subscription - the system-of-record view of a shopper's enrollment.</summary>
public class MaxioSubscription
{
    public long Id { get; set; }

    /// <summary>Maxio subscription state, e.g. "active", "trialing", "past_due", "canceled".</summary>
    public string? State { get; set; }

    /// <summary>The recurring amount for the subscribed product version, in cents.</summary>
    public long ProductPriceInCents { get; set; }

    public long BalanceInCents { get; set; }

    public long TotalRevenueInCents { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When Maxio will next attempt to capture payment. The authoritative "next billing date".</summary>
    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? TrialStartedAt { get; set; }

    public DateTimeOffset? TrialEndedAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    public string? Reference { get; set; }

    public MaxioCustomer? Customer { get; set; }

    /// <summary>Null on sites using the catalog-independent subscription experience.</summary>
    public MaxioProduct? Product { get; set; }
}
