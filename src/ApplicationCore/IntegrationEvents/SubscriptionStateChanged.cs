using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process, best-effort, after a subscription lifecycle transition (pause/resume/
/// cancel/reactivate) has been committed (UC4), carrying the old and new state.
/// </summary>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(string customerReference, int subscriptionId, SubscriptionState oldState, SubscriptionState newState)
    {
        CustomerReference = customerReference;
        SubscriptionId = subscriptionId;
        OldState = oldState;
        NewState = newState;
    }

    public string CustomerReference { get; }
    public int SubscriptionId { get; }
    public SubscriptionState OldState { get; }
    public SubscriptionState NewState { get; }
}
