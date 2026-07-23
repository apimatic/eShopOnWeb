using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces a lifecycle transition — pause, resume, cancel or reactivate — carrying old → new
/// state (UC4, step 3).
/// </summary>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(Subscription subscription, SubscriptionState previousState)
    {
        Subscription = subscription;
        PreviousState = previousState;
    }

    public Subscription Subscription { get; }
    public SubscriptionState PreviousState { get; }
    public SubscriptionState NewState => Subscription.State;
}
