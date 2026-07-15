using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>Published after a lifecycle transition (pause/resume/cancel/reactivate) commits successfully (UC4).</summary>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(string userId, int subscriptionId, string oldState, string newState)
    {
        UserId = userId;
        SubscriptionId = subscriptionId;
        OldState = oldState;
        NewState = newState;
    }

    public string UserId { get; }
    public int SubscriptionId { get; }
    public string OldState { get; }
    public string NewState { get; }
}
