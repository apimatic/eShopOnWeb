using System;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after a lifecycle transition — pause, resume, cancel or reactivate (plan.md UC4).
/// Carries old → new state. Delivery is best-effort (plan.md §2.5).
/// </summary>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(string userName, int subscriptionId, SubscriptionState previousState,
        SubscriptionState newState, SubscriptionLifecycleAction action, DateTimeOffset? effectiveAt, string? reason)
    {
        UserName = userName;
        SubscriptionId = subscriptionId;
        PreviousState = previousState;
        NewState = newState;
        Action = action;
        EffectiveAt = effectiveAt;
        Reason = reason;
    }

    public string UserName { get; }

    public int SubscriptionId { get; }

    public SubscriptionState PreviousState { get; }

    public SubscriptionState NewState { get; }

    public SubscriptionLifecycleAction Action { get; }

    /// <summary>When the transition takes effect; null when the provider did not report one.</summary>
    public DateTimeOffset? EffectiveAt { get; }

    public string? Reason { get; }
}
