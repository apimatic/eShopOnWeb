using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that a subscription completed a lifecycle transition (UC4, step 3).
/// </summary>
/// <remarks>
/// Published in-process through MediatR after the provider call has already succeeded. Delivery
/// is best-effort: a failing handler is logged and never rolls back the transition.
/// </remarks>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(
        Subscription subscription,
        SubscriptionState previousState,
        SubscriptionState newState,
        SubscriptionLifecycleAction action,
        string? reason)
    {
        Subscription = subscription;
        PreviousState = previousState;
        NewState = newState;
        Action = action;
        Reason = reason;
    }

    public Subscription Subscription { get; }

    public SubscriptionState PreviousState { get; }

    public SubscriptionState NewState { get; }

    /// <summary>The transition that was requested.</summary>
    public SubscriptionLifecycleAction Action { get; }

    /// <summary>The optional reason the actor supplied.</summary>
    public string? Reason { get; }
}
