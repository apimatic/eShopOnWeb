using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces a lifecycle transition, carrying old to new state (UC4 step 3). Published
/// in-process and best-effort after the provider call succeeds (§2.5).
/// </summary>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(Subscription subscription, SubscriptionState previousState, string action)
    {
        Subscription = subscription;
        PreviousState = previousState;
        Action = action;
    }

    public Subscription Subscription { get; }

    public SubscriptionState PreviousState { get; }

    public SubscriptionState NewState => Subscription.State;

    /// <summary>The lifecycle action that caused the transition, e.g. "pause" or "cancel".</summary>
    public string Action { get; }
}
