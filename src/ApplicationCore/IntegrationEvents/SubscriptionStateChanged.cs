using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process (best-effort, §2.5) after a lifecycle transition — pause / resume /
/// cancel / reactivate (UC4) — carrying the old → new state.
/// </summary>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(int subscriptionId, string oldState, string newState,
        CustomerSubscription subscription)
    {
        SubscriptionId = subscriptionId;
        OldState = oldState;
        NewState = newState;
        Subscription = subscription;
    }

    public int SubscriptionId { get; }

    public string OldState { get; }

    public string NewState { get; }

    public CustomerSubscription Subscription { get; }
}
