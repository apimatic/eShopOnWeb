using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>Published after a lifecycle transition — pause/resume/cancel/reactivate — is committed (UC4).</summary>
public class SubscriptionStateChanged : INotification
{
    public string CustomerReference { get; }
    public int SubscriptionId { get; }
    public string OldState { get; }
    public string NewState { get; }

    public SubscriptionStateChanged(string customerReference, int subscriptionId, string oldState, string newState)
    {
        CustomerReference = customerReference;
        SubscriptionId = subscriptionId;
        OldState = oldState;
        NewState = newState;
    }
}
