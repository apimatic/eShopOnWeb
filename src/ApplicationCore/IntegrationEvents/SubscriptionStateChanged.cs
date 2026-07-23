using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published after a lifecycle transition — pause, resume, cancel or reactivate (UC4) — carrying
/// old state to new state. Delivery is in-process and best-effort.
/// </summary>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(string userName, Subscription subscription, SubscriptionState previousState)
    {
        UserName = userName;
        Subscription = subscription;
        PreviousState = previousState;
    }

    public string UserName { get; }
    public Subscription Subscription { get; }
    public SubscriptionState PreviousState { get; }
    public SubscriptionState NewState => Subscription.State;
}
