using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a lifecycle action is illegal from the subscription's current state (UC4). The check
/// happens before any provider call, so an illegal transition never reaches the billing provider.
/// </summary>
public class InvalidSubscriptionTransitionException : Exception
{
    public InvalidSubscriptionTransitionException(int subscriptionId,
        SubscriptionLifecycleState currentState,
        SubscriptionLifecycleAction action,
        string legalActions)
        : base($"Cannot {action} subscription {subscriptionId} while it is {currentState}. Legal actions from this state: {legalActions}.")
    {
        SubscriptionId = subscriptionId;
        CurrentState = currentState;
        Action = action;
        LegalActions = legalActions;
    }

    public int SubscriptionId { get; }

    public SubscriptionLifecycleState CurrentState { get; }

    public SubscriptionLifecycleAction Action { get; }

    /// <summary>The actions that are legal from <see cref="CurrentState"/>, for surfacing back to the actor.</summary>
    public string LegalActions { get; }
}
