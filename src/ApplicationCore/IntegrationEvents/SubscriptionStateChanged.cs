using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces a lifecycle transition — pause, resume, cancel, or reactivate (UC4). Published
/// best-effort and in-process after the provider call has succeeded.
/// </summary>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(Subscription subscription,
        SubscriptionState previousState,
        SubscriptionLifecycleAction action)
    {
        Subscription = subscription;
        PreviousState = previousState;
        NewState = subscription.State;
        Action = action;
    }

    public Subscription Subscription { get; }

    public SubscriptionState PreviousState { get; }

    public SubscriptionState NewState { get; }

    public SubscriptionLifecycleAction Action { get; }
}
