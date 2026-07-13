using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>Published in-process (best-effort) after a subscription lifecycle transition (pause/resume/cancel/reactivate) succeeds.</summary>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(string userReference, int subscriptionId, string previousState, string newState)
    {
        UserReference = userReference;
        SubscriptionId = subscriptionId;
        PreviousState = previousState;
        NewState = newState;
    }

    public string UserReference { get; }
    public int SubscriptionId { get; }
    public string PreviousState { get; }
    public string NewState { get; }
}
