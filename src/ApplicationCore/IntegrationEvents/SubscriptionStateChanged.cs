using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(int subscriptionId, string userReference, string oldState, string newState)
    {
        SubscriptionId = subscriptionId;
        UserReference = userReference;
        OldState = oldState;
        NewState = newState;
    }

    public int SubscriptionId { get; }
    public string UserReference { get; }
    public string OldState { get; }
    public string NewState { get; }
}
