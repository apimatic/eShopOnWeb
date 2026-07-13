using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published after a lifecycle transition (pause/resume/cancel/reactivate, UC4) is committed
/// with the provider, carrying old → new state. Delivery is best-effort, in-process only (§2.5).
/// </summary>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(string customerReference, long subscriptionId, SubscriptionLifecycleState oldState, SubscriptionLifecycleState newState)
    {
        CustomerReference = customerReference;
        SubscriptionId = subscriptionId;
        OldState = oldState;
        NewState = newState;
    }

    public string CustomerReference { get; }
    public long SubscriptionId { get; }
    public SubscriptionLifecycleState OldState { get; }
    public SubscriptionLifecycleState NewState { get; }
}
