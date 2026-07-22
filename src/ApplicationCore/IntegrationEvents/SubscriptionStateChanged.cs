using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after a lifecycle transition, carrying old to new state (UC4 step 3).
/// Delivery is best-effort (plan.md §2.5).
/// </summary>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(Subscription subscription, SubscriptionState previousState,
        SubscriptionLifecycleAction action)
    {
        Subscription = subscription;
        PreviousState = previousState;
        Action = action;
    }

    public Subscription Subscription { get; }
    public SubscriptionState PreviousState { get; }
    public SubscriptionState NewState => Subscription.State;
    public SubscriptionLifecycleAction Action { get; }
}
