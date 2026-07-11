using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>Published in-process after a lifecycle transition (pause/resume/cancel/reactivate - UC4).</summary>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(int subscriptionId, string buyerId, string previousState, string newState)
    {
        SubscriptionId = subscriptionId;
        BuyerId = buyerId;
        PreviousState = previousState;
        NewState = newState;
    }

    public int SubscriptionId { get; }
    public string BuyerId { get; }
    public string PreviousState { get; }
    public string NewState { get; }
}
