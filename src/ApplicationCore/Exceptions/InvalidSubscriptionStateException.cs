using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a requested lifecycle or plan-change action is not legal from the subscription's
/// current state (e.g. resuming a subscription that is not on hold).
/// </summary>
public class InvalidSubscriptionStateException : Exception
{
    public InvalidSubscriptionStateException(int subscriptionId, SubscriptionState currentState, string requestedAction)
        : base($"Cannot perform '{requestedAction}' on subscription {subscriptionId}: current state is '{currentState}'")
    {
        CurrentState = currentState;
    }

    public SubscriptionState CurrentState { get; private set; }
}
