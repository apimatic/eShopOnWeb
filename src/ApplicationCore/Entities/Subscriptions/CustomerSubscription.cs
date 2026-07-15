using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;

public class CustomerSubscription
{
    public CustomerSubscription(int id, string customerReference, string state, string planHandle,
        string planName, long priceInCents, DateTimeOffset? currentPeriodStartedAt,
        DateTimeOffset? currentPeriodEndsAt, bool cancelAtEndOfPeriod, string? nextPlanHandle,
        long balanceInCents)
    {
        Id = id;
        CustomerReference = customerReference;
        State = state;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        CurrentPeriodStartedAt = currentPeriodStartedAt;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        CancelAtEndOfPeriod = cancelAtEndOfPeriod;
        NextPlanHandle = nextPlanHandle;
        BalanceInCents = balanceInCents;
    }

    public int Id { get; }
    public string CustomerReference { get; }

    // Raw wire value of the billing provider's subscription state — compare against SubscriptionStates.*.
    public string State { get; }
    public string PlanHandle { get; }
    public string PlanName { get; }
    public long PriceInCents { get; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; }
    public bool CancelAtEndOfPeriod { get; }

    // Non-null when a "change at next renewal" (no proration) plan change is pending.
    public string? NextPlanHandle { get; }
    public long BalanceInCents { get; }
}
