using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a lifecycle action is illegal from the subscription's current state (UC4).
/// No provider call is made in that case.
/// </summary>
public class InvalidSubscriptionTransitionException : Exception
{
    public InvalidSubscriptionTransitionException(int subscriptionId, SubscriptionLifecycleAction action,
        SubscriptionState currentState, string legalActions)
        : base($"Cannot {action} subscription {subscriptionId} while it is {currentState}. Legal actions: {legalActions}.")
    {
        SubscriptionId = subscriptionId;
        Action = action;
        CurrentState = currentState;
        LegalActions = legalActions;
    }

    public int SubscriptionId { get; }
    public SubscriptionLifecycleAction Action { get; }
    public SubscriptionState CurrentState { get; }
    public string LegalActions { get; }
}
