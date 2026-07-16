using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested lifecycle/plan-change action is not legal from the subscription's current state
/// (UC3/UC4). <see cref="CurrentState"/> reflects the provider's live state at the time of the check.
/// </summary>
public class InvalidSubscriptionStateException : Exception
{
    public InvalidSubscriptionStateException(int subscriptionId, SubscriptionState currentState, string attemptedAction)
        : base($"Subscription {subscriptionId} cannot {attemptedAction} while it is {currentState}.")
    {
        SubscriptionId = subscriptionId;
        CurrentState = currentState;
        AttemptedAction = attemptedAction;
    }

    public int SubscriptionId { get; }
    public SubscriptionState CurrentState { get; }
    public string AttemptedAction { get; }
}
