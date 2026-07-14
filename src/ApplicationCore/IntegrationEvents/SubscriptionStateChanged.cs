using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>Published in-process (best-effort, §2.5) after a lifecycle transition — pause/resume/cancel/reactivate (UC4).</summary>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(string buyerId, int subscriptionId, SubscriptionStatus oldState, SubscriptionStatus newState)
    {
        BuyerId = buyerId;
        SubscriptionId = subscriptionId;
        OldState = oldState;
        NewState = newState;
    }

    public string BuyerId { get; }
    public int SubscriptionId { get; }
    public SubscriptionStatus OldState { get; }
    public SubscriptionStatus NewState { get; }
}
