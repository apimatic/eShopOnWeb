namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// An enrolled subscription as confirmed by the billing system of record.
/// </summary>
public record UserSubscription
{
    /// <summary>Maxio subscription id.</summary>
    public long Id { get; init; }

    /// <summary>Application supplied reference of this enrollment (idempotency anchor).</summary>
    public string? Reference { get; init; }

    public SubscriptionState State { get; init; }

    /// <summary>Maxio customer id the subscription belongs to.</summary>
    public long CustomerId { get; init; }

    public string PlanHandle { get; init; } = string.Empty;

    public string? PlanName { get; init; }

    /// <summary>Recurring amount of the enrolled plan, in cents.</summary>
    public long PriceInCents { get; init; }

    public int Interval { get; init; }

    public string? IntervalUnit { get; init; }

    /// <summary>Maxio payment collection method in force ("automatic", "remittance", ...).</summary>
    public string? PaymentCollectionMethod { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    /// <summary>When the current period ends - i.e. the date the next renewal is assessed.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When Maxio will next attempt to capture payment.</summary>
    public DateTimeOffset? NextAssessmentAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public long? BalanceInCents { get; init; }

    public decimal Price => PriceInCents / 100m;

    /// <summary>
    /// The date the shopper is next billed. Maxio reports the upcoming renewal both as
    /// <c>current_period_ends_at</c> and <c>next_assessment_at</c>; the assessment date wins
    /// because it reflects renewal retries after a failed capture.
    /// </summary>
    public DateTimeOffset? NextBillingAt => NextAssessmentAt ?? CurrentPeriodEndsAt;

    public bool IsAwaitingPayment => State is SubscriptionState.PastDue or SubscriptionState.Unpaid or SubscriptionState.SoftFailure;
}
