using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The subscription exists but is not active, so usage cannot be recorded against it
/// (UC2 preconditions/failure scenarios).
/// </summary>
public class SubscriptionNotActiveException : Exception
{
    public SubscriptionNotActiveException(long subscriptionId, SubscriptionLifecycleState currentState)
        : base($"Subscription {subscriptionId} is not active (current state: {currentState}); usage cannot be recorded.")
    {
        CurrentState = currentState;
    }

    public SubscriptionLifecycleState CurrentState { get; }
}
