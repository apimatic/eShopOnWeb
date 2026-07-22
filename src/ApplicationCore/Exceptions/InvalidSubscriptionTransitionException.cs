using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A lifecycle transition was requested that is not legal from the subscription's current state.
/// Carries both the current state and the legal alternatives so the caller can tell the customer
/// what they can do instead (UC4). Thrown before any provider call is made.
/// </summary>
public class InvalidSubscriptionTransitionException : Exception
{
    public InvalidSubscriptionTransitionException(int subscriptionId,
        SubscriptionLifecycleAction requestedAction,
        SubscriptionState currentState,
        IReadOnlyCollection<SubscriptionLifecycleAction> allowedActions)
        : base(BuildMessage(subscriptionId, requestedAction, currentState, allowedActions))
    {
        SubscriptionId = subscriptionId;
        RequestedAction = requestedAction;
        CurrentState = currentState;
        AllowedActions = allowedActions;
    }

    public int SubscriptionId { get; }

    public SubscriptionLifecycleAction RequestedAction { get; }

    public SubscriptionState CurrentState { get; }

    public IReadOnlyCollection<SubscriptionLifecycleAction> AllowedActions { get; }

    private static string BuildMessage(int subscriptionId,
        SubscriptionLifecycleAction requestedAction,
        SubscriptionState currentState,
        IReadOnlyCollection<SubscriptionLifecycleAction> allowedActions)
    {
        var allowed = allowedActions.Count == 0
            ? "no actions are available"
            : $"allowed actions are: {string.Join(", ", allowedActions.Select(a => a.ToString()))}";

        return $"Cannot {requestedAction} subscription {subscriptionId} while it is {currentState} — {allowed}.";
    }
}
