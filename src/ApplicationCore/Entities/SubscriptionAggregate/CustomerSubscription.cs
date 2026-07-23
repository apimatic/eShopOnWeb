using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// An eShopOnWeb customer's enrollment in a plan, as the billing provider currently sees it.
/// The provider is the system of record; this is a read model over its state.
/// </summary>
public class CustomerSubscription
{
    public CustomerSubscription(int id, SubscriptionState state, int customerId, string? customerReference,
        string planHandle, string planName, decimal planPrice, DateTimeOffset? currentPeriodEndsAt,
        bool cancelAtEndOfPeriod, DateTimeOffset? delayedCancelAt, string? nextPlanHandle)
    {
        Guard.Against.NegativeOrZero(id, nameof(id));

        Id = id;
        State = state;
        CustomerId = customerId;
        CustomerReference = customerReference;
        PlanHandle = planHandle;
        PlanName = planName;
        PlanPrice = planPrice;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        CancelAtEndOfPeriod = cancelAtEndOfPeriod;
        DelayedCancelAt = delayedCancelAt;
        NextPlanHandle = nextPlanHandle;
    }

    public int Id { get; private set; }

    public SubscriptionState State { get; private set; }

    public int CustomerId { get; private set; }

    /// <summary>The eShopOnWeb user reference the owning customer was created against.</summary>
    public string? CustomerReference { get; private set; }

    public string PlanHandle { get; private set; }

    public string PlanName { get; private set; }

    /// <summary>The recurring price currently subscribed to, in major units (e.g. 299.00 dollars).</summary>
    public decimal PlanPrice { get; private set; }

    /// <summary>When the current billing period ends, i.e. the next billing date.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; private set; }

    public bool CancelAtEndOfPeriod { get; private set; }

    /// <summary>When a requested end-of-period cancellation takes effect, if one is pending.</summary>
    public DateTimeOffset? DelayedCancelAt { get; private set; }

    /// <summary>The plan a scheduled (delayed) plan change will move this subscription to at renewal.</summary>
    public string? NextPlanHandle { get; private set; }

    /// <summary>
    /// A subscription is live when the provider is billing it. Only live subscriptions accept
    /// usage, plan changes, or a pause.
    /// </summary>
    public bool IsActive => State == SubscriptionState.Active || State == SubscriptionState.Trialing;
}
