using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a lifecycle or plan-change action is requested that is illegal from the
/// subscription's current state (e.g. resuming a subscription that isn't paused). Raised by a
/// local precondition check, before any provider call is made.
/// </summary>
public class InvalidSubscriptionStateException : Exception
{
    public InvalidSubscriptionStateException(int subscriptionId, SubscriptionStatus currentState, string attemptedAction)
        : base($"Subscription {subscriptionId} cannot {attemptedAction} while in state {currentState}.")
    {
        SubscriptionId = subscriptionId;
        CurrentState = currentState;
    }

    public int SubscriptionId { get; }
    public SubscriptionStatus CurrentState { get; }
}
