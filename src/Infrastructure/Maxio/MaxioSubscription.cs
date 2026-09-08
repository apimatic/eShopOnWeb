using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A Maxio subscription. Mirrors the subset of <c>Subscription.yaml</c> (Maxio OpenAPI spec)
/// that this integration consumes.
/// </summary>
public class MaxioSubscription
{
    public long Id { get; set; }

    /// <summary>The subscription state; see <c>Subscription-State.yaml</c> in the Maxio spec.</summary>
    public string? State { get; set; }

    /// <summary>The application-provided reference for the subscription (unique per live subscription).</summary>
    public string? Reference { get; set; }

    public string? Currency { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    public long? BalanceInCents { get; set; }

    public long? ProductPriceInCents { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>The date/time at which the next (renewal) assessment is scheduled.</summary>
    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public bool? CancelAtEndOfPeriod { get; set; }

    public MaxioCustomer? Customer { get; set; }

    public MaxioProduct? Product { get; set; }
}
