using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription held by an eShopOnWeb user, as recorded by the billing provider.
/// The provider is the system of record: everything here is a projection of provider state.
/// </summary>
public class CustomerSubscription
{
    public CustomerSubscription(long id, string? reference, string state, long customerId,
        string? customerReference, string? planHandle, string? planName, string? pricePointHandle,
        long priceInCents, int? intervalLength, string? intervalUnit,
        DateTimeOffset? currentPeriodStartedAt, DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextAssessmentAt, DateTimeOffset? trialEndedAt,
        DateTimeOffset? activatedAt, DateTimeOffset? canceledAt, DateTimeOffset? createdAt,
        long balanceInCents)
    {
        Id = id;
        Reference = reference;
        State = state;
        CustomerId = customerId;
        CustomerReference = customerReference;
        PlanHandle = planHandle;
        PlanName = planName;
        PricePointHandle = pricePointHandle;
        PriceInCents = priceInCents;
        IntervalLength = intervalLength;
        IntervalUnit = intervalUnit;
        CurrentPeriodStartedAt = currentPeriodStartedAt;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextAssessmentAt = nextAssessmentAt;
        TrialEndedAt = trialEndedAt;
        ActivatedAt = activatedAt;
        CanceledAt = canceledAt;
        CreatedAt = createdAt;
        BalanceInCents = balanceInCents;
    }

    /// <summary>The provider's subscription id.</summary>
    public long Id { get; }

    /// <summary>The reference this application assigned to the subscription at signup.</summary>
    public string? Reference { get; }

    /// <summary>Provider subscription state, e.g. "active", "trialing", "past_due", "canceled".</summary>
    public string State { get; }

    public long CustomerId { get; }

    public string? CustomerReference { get; }

    public string? PlanHandle { get; }

    public string? PlanName { get; }

    /// <summary>Handle of the price point the subscription is billed on, when the provider reports one.</summary>
    public string? PricePointHandle { get; }

    /// <summary>The recurring amount for this subscription, in minor units.</summary>
    public long PriceInCents { get; }

    public decimal Price => decimal.Divide(PriceInCents, 100m);

    public int? IntervalLength { get; }

    public string? IntervalUnit { get; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>
    /// When the provider will next attempt to capture payment. Diverges from
    /// <see cref="CurrentPeriodEndsAt"/> only when a renewal failed and is being retried.
    /// </summary>
    public DateTimeOffset? NextAssessmentAt { get; }

    /// <summary>
    /// The date the shopper should be told about: the next assessment if one is scheduled,
    /// otherwise the end of the current period.
    /// </summary>
    public DateTimeOffset? NextBillingAt => NextAssessmentAt ?? CurrentPeriodEndsAt;

    public DateTimeOffset? TrialEndedAt { get; }

    public DateTimeOffset? ActivatedAt { get; }

    public DateTimeOffset? CanceledAt { get; }

    public DateTimeOffset? CreatedAt { get; }

    public long BalanceInCents { get; }

    /// <summary>
    /// True while the subscription entitles the shopper to the product. Mirrors the provider's
    /// documented "Live States" plus <c>past_due</c>, which keeps access during dunning.
    /// </summary>
    public bool IsLive => SubscriptionStates.IsLive(State);
}
