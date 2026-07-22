using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a lifecycle action is not legal from the subscription's current state. Thrown before
/// any provider call is made, and carries the actions that are legal so the caller can guide the user.
/// </summary>
public class InvalidSubscriptionTransitionException : Exception
{
    public InvalidSubscriptionTransitionException(int subscriptionId,
        SubscriptionState currentState,
        SubscriptionLifecycleAction requestedAction,
        IEnumerable<SubscriptionLifecycleAction> allowedActions)
        : base(BuildMessage(subscriptionId, currentState, requestedAction, allowedActions))
    {
        SubscriptionId = subscriptionId;
        CurrentState = currentState;
        RequestedAction = requestedAction;
        AllowedActions = allowedActions.ToList();
    }

    public int SubscriptionId { get; }

    public SubscriptionState CurrentState { get; }

    public SubscriptionLifecycleAction RequestedAction { get; }

    public IReadOnlyCollection<SubscriptionLifecycleAction> AllowedActions { get; }

    private static string BuildMessage(int subscriptionId,
        SubscriptionState currentState,
        SubscriptionLifecycleAction requestedAction,
        IEnumerable<SubscriptionLifecycleAction> allowedActions)
    {
        var allowed = allowedActions.ToList();
        var legal = allowed.Count == 0 ? "none" : string.Join(", ", allowed);
        return $"Subscription {subscriptionId} is {currentState} and cannot be {requestedAction}. Legal actions: {legal}.";
    }
}
