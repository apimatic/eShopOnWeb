using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces a lifecycle transition (pause, resume, cancel, reactivate) carrying old to new state.
/// Published in-process, best-effort, after the provider call succeeds.
/// </summary>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(string customerReference, int subscriptionId, SubscriptionState oldState,
        SubscriptionState newState, string action)
    {
        CustomerReference = customerReference;
        SubscriptionId = subscriptionId;
        OldState = oldState;
        NewState = newState;
        Action = action;
    }

    public string CustomerReference { get; }

    public int SubscriptionId { get; }

    public SubscriptionState OldState { get; }

    public SubscriptionState NewState { get; }

    /// <summary>The lifecycle action that caused the transition, e.g. <c>pause</c>.</summary>
    public string Action { get; }
}
