using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

// Published in-process, best-effort, after a lifecycle transition (UC4: pause/resume/cancel/reactivate).
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(string customerReference, int subscriptionId, string oldState, string newState)
    {
        CustomerReference = customerReference;
        SubscriptionId = subscriptionId;
        OldState = oldState;
        NewState = newState;
    }

    public string CustomerReference { get; }
    public int SubscriptionId { get; }
    public string OldState { get; }
    public string NewState { get; }
}
