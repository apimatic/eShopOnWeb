using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process (best-effort — plan.md §2.5) after UC4 applies a pause/resume/cancel/reactivate
/// transition, carrying old -&gt; new state.
/// </summary>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(int subscriptionId, SubscriptionState previousState, SubscriptionState newState)
    {
        SubscriptionId = subscriptionId;
        PreviousState = previousState;
        NewState = newState;
    }

    public int SubscriptionId { get; }
    public SubscriptionState PreviousState { get; }
    public SubscriptionState NewState { get; }
}
