using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested lifecycle transition is not legal from the subscription's current state. Carries the
/// current state and the transitions that <em>are</em> legal, so the caller can tell the actor what to do
/// instead (plan.md UC4, "illegal transition" failure scenario). No provider call is made.
/// </summary>
public class InvalidSubscriptionTransitionException : Exception
{
    public InvalidSubscriptionTransitionException(
        int subscriptionId,
        SubscriptionState currentState,
        SubscriptionLifecycleAction requestedAction,
        IReadOnlyCollection<SubscriptionLifecycleAction> allowedActions)
        : base(BuildMessage(subscriptionId, currentState, requestedAction, allowedActions))
    {
        SubscriptionId = subscriptionId;
        CurrentState = currentState;
        RequestedAction = requestedAction;
        AllowedActions = allowedActions;
    }

    public int SubscriptionId { get; }

    public SubscriptionState CurrentState { get; }

    public SubscriptionLifecycleAction RequestedAction { get; }

    public IReadOnlyCollection<SubscriptionLifecycleAction> AllowedActions { get; }

    private static string BuildMessage(
        int subscriptionId,
        SubscriptionState currentState,
        SubscriptionLifecycleAction requestedAction,
        IReadOnlyCollection<SubscriptionLifecycleAction> allowedActions)
    {
        var allowed = allowedActions.Count == 0
            ? "no transitions are available"
            : $"available transitions are {string.Join(", ", allowedActions.Select(a => a.ToString()))}";

        return $"Subscription {subscriptionId} is {currentState}, so the {requestedAction} action is not " +
               $"available — {allowed}.";
    }
}
