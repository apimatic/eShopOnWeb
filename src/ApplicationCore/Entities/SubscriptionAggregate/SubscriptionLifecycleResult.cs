using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The outcome of a lifecycle transition: old → new state and when it takes effect (UC4 step 4).
/// </summary>
public class SubscriptionLifecycleResult
{
    public SubscriptionLifecycleResult(Subscription subscription,
        SubscriptionLifecycleAction action,
        string previousState,
        DateTimeOffset? effectiveAt)
    {
        Subscription = subscription;
        Action = action;
        PreviousState = previousState;
        EffectiveAt = effectiveAt;
    }

    public Subscription Subscription { get; }
    public SubscriptionLifecycleAction Action { get; }
    public string PreviousState { get; }

    /// <summary>The state the provider reports after the transition — the provider is truth.</summary>
    public string NewState => Subscription.State;
    public DateTimeOffset? EffectiveAt { get; }
}
