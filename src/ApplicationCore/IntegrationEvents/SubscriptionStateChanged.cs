using System;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after a subscription lifecycle transition (pause / resume / cancel / reactivate).
/// </summary>
/// <remarks>
/// Delivery is best-effort and in-process only; a handler failure never reverses the transition.
/// </remarks>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(
        string userReference,
        BillingSubscription subscription,
        SubscriptionState previousState,
        SubscriptionState newState,
        SubscriptionLifecycleAction action,
        DateTimeOffset? effectiveAt)
    {
        UserReference = userReference;
        Subscription = subscription;
        PreviousState = previousState;
        NewState = newState;
        Action = action;
        EffectiveAt = effectiveAt;
    }

    public string UserReference { get; }

    public BillingSubscription Subscription { get; }

    public SubscriptionState PreviousState { get; }

    public SubscriptionState NewState { get; }

    public SubscriptionLifecycleAction Action { get; }

    /// <summary>When the transition takes effect — the period boundary for an end-of-period cancellation.</summary>
    public DateTimeOffset? EffectiveAt { get; }
}
