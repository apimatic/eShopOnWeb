using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A customer's recurring subscription. The billing provider is the system of record: eShopOnWeb reads
/// this aggregate back from the provider on demand rather than persisting it, because the userId ↔
/// provider mapping is stateless and idempotent on <see cref="CustomerReference"/> (plan.md §8).
/// <see cref="BaseEntity.Id"/> therefore carries the provider's subscription identifier.
/// </summary>
public class Subscription : BaseEntity, IAggregateRoot
{
    public Subscription(int id, string customerReference, int customerId, string planHandle, string planName,
        decimal planPrice, SubscriptionState state, string providerState, DateTimeOffset? currentPeriodStart,
        DateTimeOffset? currentPeriodEnd, DateTimeOffset? nextAssessmentAt, DateTimeOffset? cancellationScheduledAt)
    {
        Id = id;
        CustomerReference = customerReference;
        CustomerId = customerId;
        PlanHandle = planHandle;
        PlanName = planName;
        PlanPrice = planPrice;
        State = state;
        ProviderState = providerState;
        CurrentPeriodStart = currentPeriodStart;
        CurrentPeriodEnd = currentPeriodEnd;
        NextAssessmentAt = nextAssessmentAt;
        CancellationScheduledAt = cancellationScheduledAt;
    }

    /// <summary>The eShopOnWeb user name (email) this subscription belongs to.</summary>
    public string CustomerReference { get; }

    /// <summary>The provider-side customer identifier.</summary>
    public int CustomerId { get; }

    public string PlanHandle { get; }

    public string PlanName { get; }

    /// <summary>Recurring plan price in dollars.</summary>
    public decimal PlanPrice { get; }

    public SubscriptionState State { get; }

    /// <summary>The provider's own state string, preserved verbatim for diagnostics and support.</summary>
    public string ProviderState { get; }

    public DateTimeOffset? CurrentPeriodStart { get; }

    public DateTimeOffset? CurrentPeriodEnd { get; }

    /// <summary>When the provider will next assess (bill) this subscription.</summary>
    public DateTimeOffset? NextAssessmentAt { get; }

    /// <summary>Set when a cancellation is pending at the end of the current period.</summary>
    public DateTimeOffset? CancellationScheduledAt { get; }

    /// <summary>True when a cancellation has been scheduled but has not taken effect yet.</summary>
    public bool CancellationPending => CancellationScheduledAt.HasValue && State != SubscriptionState.Cancelled;

    /// <summary>Only a live subscription may accrue metered usage (plan.md UC2 preconditions).</summary>
    public bool CanRecordUsage => State is SubscriptionState.Active or SubscriptionState.Trialing;

    /// <summary>
    /// The lifecycle actions that are legal from the current state. UC4 rejects anything outside this
    /// set before making a provider call.
    /// </summary>
    public IReadOnlyCollection<SubscriptionLifecycleAction> AllowedActions => State switch
    {
        SubscriptionState.Active or SubscriptionState.Trialing => CancellationPending
            ? new[] { SubscriptionLifecycleAction.CancelImmediately, SubscriptionLifecycleAction.Pause }
            : new[]
            {
                SubscriptionLifecycleAction.Pause,
                SubscriptionLifecycleAction.CancelImmediately,
                SubscriptionLifecycleAction.CancelAtEndOfPeriod
            },
        SubscriptionState.Paused => new[]
        {
            SubscriptionLifecycleAction.Resume,
            SubscriptionLifecycleAction.CancelImmediately
        },
        SubscriptionState.PastDue or SubscriptionState.Suspended => new[]
        {
            SubscriptionLifecycleAction.CancelImmediately,
            SubscriptionLifecycleAction.CancelAtEndOfPeriod
        },
        SubscriptionState.Cancelled or SubscriptionState.Expired => new[]
        {
            SubscriptionLifecycleAction.Reactivate
        },
        _ => Array.Empty<SubscriptionLifecycleAction>()
    };

    /// <summary>A plan change is only meaningful while the subscription is live.</summary>
    public bool CanChangePlan => State is SubscriptionState.Active or SubscriptionState.Trialing or SubscriptionState.Paused;

    public bool Allows(SubscriptionLifecycleAction action)
    {
        foreach (var allowed in AllowedActions)
        {
            if (allowed == action)
            {
                return true;
            }
        }

        return false;
    }
}
