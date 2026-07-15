using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(string userId, int subscriptionId, BillingSubscriptionState oldState, BillingSubscriptionState newState)
    {
        UserId = userId;
        SubscriptionId = subscriptionId;
        OldState = oldState;
        NewState = newState;
    }

    public string UserId { get; }
    public int SubscriptionId { get; }
    public BillingSubscriptionState OldState { get; }
    public BillingSubscriptionState NewState { get; }
}
