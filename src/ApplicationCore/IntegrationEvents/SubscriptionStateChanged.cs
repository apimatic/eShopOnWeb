using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process (best-effort, §2.5) after a lifecycle transition commits (UC4): carries
/// old → new state.
/// </summary>
public class SubscriptionStateChanged : INotification
{
    public string UserReference { get; }
    public int SubscriptionId { get; }
    public SubscriptionState OldState { get; }
    public SubscriptionState NewState { get; }

    public SubscriptionStateChanged(string userReference, int subscriptionId, SubscriptionState oldState, SubscriptionState newState)
    {
        UserReference = userReference;
        SubscriptionId = subscriptionId;
        OldState = oldState;
        NewState = newState;
    }
}
