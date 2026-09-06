using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription as held by the billing system of record.
/// </summary>
public class CustomerSubscription
{
    public CustomerSubscription(
        long id,
        string? reference,
        SubscriptionState state,
        string? rawState,
        string? planHandle,
        string? planName,
        long productPriceInCents,
        string? currency,
        int? interval,
        string? intervalUnit,
        long customerId,
        string? customerReference,
        DateTimeOffset? currentPeriodStartedAt,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextAssessmentAt,
        DateTimeOffset? trialStartedAt,
        DateTimeOffset? trialEndedAt,
        DateTimeOffset? activatedAt,
        DateTimeOffset? canceledAt,
        DateTimeOffset? expiresAt,
        DateTimeOffset? createdAt,
        bool? cancelAtEndOfPeriod,
        string? paymentCollectionMethod,
        long balanceInCents)
    {
        Id = id;
        Reference = reference;
        State = state;
        RawState = rawState;
        PlanHandle = planHandle;
        PlanName = planName;
        ProductPriceInCents = productPriceInCents;
        Currency = currency;
        Interval = interval;
        IntervalUnit = intervalUnit;
        CustomerId = customerId;
        CustomerReference = customerReference;
        CurrentPeriodStartedAt = currentPeriodStartedAt;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextAssessmentAt = nextAssessmentAt;
        TrialStartedAt = trialStartedAt;
        TrialEndedAt = trialEndedAt;
        ActivatedAt = activatedAt;
        CanceledAt = canceledAt;
        ExpiresAt = expiresAt;
        CreatedAt = createdAt;
        CancelAtEndOfPeriod = cancelAtEndOfPeriod;
        PaymentCollectionMethod = paymentCollectionMethod;
        BalanceInCents = balanceInCents;
    }

    public long Id { get; }

    /// <summary>The eShopOnWeb-side idempotency reference stored on the subscription.</summary>
    public string? Reference { get; }

    public SubscriptionState State { get; }

    /// <summary>The state string exactly as returned by the provider, preserved for diagnostics.</summary>
    public string? RawState { get; }

    public string? PlanHandle { get; }

    public string? PlanName { get; }

    public long ProductPriceInCents { get; }

    public string? Currency { get; }

    public int? Interval { get; }

    public string? IntervalUnit { get; }

    public long CustomerId { get; }

    public string? CustomerReference { get; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>When the provider will next attempt to bill this subscription.</summary>
    public DateTimeOffset? NextAssessmentAt { get; }

    public DateTimeOffset? TrialStartedAt { get; }

    public DateTimeOffset? TrialEndedAt { get; }

    public DateTimeOffset? ActivatedAt { get; }

    public DateTimeOffset? CanceledAt { get; }

    public DateTimeOffset? ExpiresAt { get; }

    public DateTimeOffset? CreatedAt { get; }

    public bool? CancelAtEndOfPeriod { get; }

    public string? PaymentCollectionMethod { get; }

    public long BalanceInCents { get; }

    public decimal ProductPrice => decimal.Divide(ProductPriceInCents, 100m);

    /// <summary>
    /// The date the shopper is next billed. Falls back to the end of the current period when the
    /// provider has not scheduled an assessment (for example while a subscription awaits signup).
    /// </summary>
    public DateTimeOffset? NextBillingAt => NextAssessmentAt ?? CurrentPeriodEndsAt;

    public bool IsCurrent => State.IsCurrent();
}
