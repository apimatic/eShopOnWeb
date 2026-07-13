using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A live view of a customer's subscription as currently known to the billing provider.
/// The provider is the system of record; this is never persisted locally. // ValueObject
/// </summary>
public class SubscriptionDetails
{
    public SubscriptionDetails(
        int id,
        string customerReference,
        SubscriptionState state,
        string productHandle,
        string productName,
        decimal priceInCents,
        string intervalUnit,
        int interval,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextAssessmentAt,
        bool cancelAtEndOfPeriod,
        DateTimeOffset? delayedCancelAt,
        DateTimeOffset? onHoldAt,
        DateTimeOffset? automaticallyResumeAt)
    {
        Guard.Against.NegativeOrZero(id, nameof(id));
        Guard.Against.NullOrEmpty(customerReference, nameof(customerReference));
        Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));
        Guard.Against.NullOrEmpty(productName, nameof(productName));
        Guard.Against.NullOrEmpty(intervalUnit, nameof(intervalUnit));

        Id = id;
        CustomerReference = customerReference;
        State = state;
        ProductHandle = productHandle;
        ProductName = productName;
        PriceInCents = priceInCents;
        IntervalUnit = intervalUnit;
        Interval = interval;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextAssessmentAt = nextAssessmentAt;
        CancelAtEndOfPeriod = cancelAtEndOfPeriod;
        DelayedCancelAt = delayedCancelAt;
        OnHoldAt = onHoldAt;
        AutomaticallyResumeAt = automaticallyResumeAt;
    }

    public int Id { get; private set; }
    public string CustomerReference { get; private set; }
    public SubscriptionState State { get; private set; }
    public string ProductHandle { get; private set; }
    public string ProductName { get; private set; }
    public decimal PriceInCents { get; private set; }
    public string IntervalUnit { get; private set; }
    public int Interval { get; private set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; private set; }
    public DateTimeOffset? NextAssessmentAt { get; private set; }
    public bool CancelAtEndOfPeriod { get; private set; }
    public DateTimeOffset? DelayedCancelAt { get; private set; }
    public DateTimeOffset? OnHoldAt { get; private set; }
    public DateTimeOffset? AutomaticallyResumeAt { get; private set; }

    public bool IsActiveOrTrialing => State is SubscriptionState.Active or SubscriptionState.Trialing;
}
