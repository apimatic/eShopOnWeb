using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested lifecycle action is not legal from the subscription's current state. Thrown
/// before any provider call is made, and carries the current state so the caller can explain why.
/// </summary>
public class InvalidSubscriptionTransitionException : Exception
{
    public InvalidSubscriptionTransitionException(int subscriptionId, SubscriptionState currentState, string action)
        : base($"Cannot {action} subscription {subscriptionId} while it is {currentState}.")
    {
        SubscriptionId = subscriptionId;
        CurrentState = currentState;
        Action = action;
    }

    public int SubscriptionId { get; }

    public SubscriptionState CurrentState { get; }

    public string Action { get; }
}
