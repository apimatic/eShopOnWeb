using System;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after a lifecycle transition succeeds, carrying old → new state (UC4 step 3).
/// </summary>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(int subscriptionId,
        SubscriptionLifecycleAction action,
        string previousState,
        string newState,
        DateTimeOffset? effectiveAt)
    {
        SubscriptionId = subscriptionId;
        Action = action;
        PreviousState = previousState;
        NewState = newState;
        EffectiveAt = effectiveAt;
    }

    public int SubscriptionId { get; }
    public SubscriptionLifecycleAction Action { get; }
    public string PreviousState { get; }
    public string NewState { get; }
    public DateTimeOffset? EffectiveAt { get; }
}
