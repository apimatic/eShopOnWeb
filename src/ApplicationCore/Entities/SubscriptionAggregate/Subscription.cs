using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// eShopOnWeb's view of a recurring subscription held by the billing provider.
/// </summary>
/// <remarks>
/// The billing provider is the system of record. This aggregate is projected from the provider on
/// every read rather than persisted, so the eShopOnWeb user is linked to the provider through the
/// idempotent <see cref="CustomerReference"/> instead of through a stored row. <see cref="BaseEntity.Id"/>
/// therefore carries no meaning here; use <see cref="ProviderSubscriptionId"/> to address the
/// subscription with the provider.
/// </remarks>
public class Subscription : BaseEntity, IAggregateRoot
{
    public Subscription(int providerSubscriptionId,
        int providerCustomerId,
        string customerReference,
        BillingPlan plan,
        SubscriptionState state,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextBillingAt,
        bool cancelAtEndOfPeriod,
        string? scheduledPlanHandle)
    {
        Guard.Against.Null(customerReference, nameof(customerReference));
        Guard.Against.Null(plan, nameof(plan));

        ProviderSubscriptionId = providerSubscriptionId;
        ProviderCustomerId = providerCustomerId;
        CustomerReference = customerReference;
        Plan = plan;
        State = state;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextBillingAt = nextBillingAt;
        CancelAtEndOfPeriod = cancelAtEndOfPeriod;
        ScheduledPlanHandle = scheduledPlanHandle;
    }

    /// <summary>The provider's identifier for this subscription.</summary>
    public int ProviderSubscriptionId { get; private set; }

    /// <summary>The provider's identifier for the owning customer.</summary>
    public int ProviderCustomerId { get; private set; }

    /// <summary>The eShopOnWeb identity (email/username) this subscription belongs to.</summary>
    public string CustomerReference { get; private set; }

    public BillingPlan Plan { get; private set; }

    public SubscriptionState State { get; private set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; private set; }

    public DateTimeOffset? NextBillingAt { get; private set; }

    /// <summary>True when a cancellation has been scheduled for the end of the current period.</summary>
    public bool CancelAtEndOfPeriod { get; private set; }

    /// <summary>Handle of a plan change that has been deferred to the next renewal, if any.</summary>
    public string? ScheduledPlanHandle { get; private set; }

    /// <summary>A subscription is billable — and can therefore accrue metered usage — while it is live.</summary>
    public bool IsActive => State is SubscriptionState.Active or SubscriptionState.Trialing;

    public bool CanPause => State is SubscriptionState.Active or SubscriptionState.Trialing;

    public bool CanResume => State is SubscriptionState.Paused;

    public bool CanCancel => State is not (SubscriptionState.Canceled or SubscriptionState.Expired);

    public bool CanReactivate => State is SubscriptionState.Canceled or SubscriptionState.Expired;

    public bool CanChangePlan => State is SubscriptionState.Active or SubscriptionState.Trialing
        or SubscriptionState.PastDue or SubscriptionState.Paused;
}
