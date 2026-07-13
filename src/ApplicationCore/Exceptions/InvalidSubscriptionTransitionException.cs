using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested lifecycle transition (pause/resume/cancel/reactivate) is not legal from the
/// subscription's current state. No provider call is made when this is thrown (UC4 failure scenarios).
/// </summary>
public class InvalidSubscriptionTransitionException : Exception
{
    public InvalidSubscriptionTransitionException(long subscriptionId, SubscriptionLifecycleState currentState, string requestedTransition)
        : base($"Cannot {requestedTransition} subscription {subscriptionId}: current state is {currentState}")
    {
        CurrentState = currentState;
    }

    public SubscriptionLifecycleState CurrentState { get; }
}
