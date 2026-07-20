using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(string userReference, int subscriptionId, SubscriptionLifecycleState oldState, SubscriptionLifecycleState newState)
    {
        UserReference = userReference;
        SubscriptionId = subscriptionId;
        OldState = oldState;
        NewState = newState;
    }

    public string UserReference { get; }
    public int SubscriptionId { get; }
    public SubscriptionLifecycleState OldState { get; }
    public SubscriptionLifecycleState NewState { get; }
}
