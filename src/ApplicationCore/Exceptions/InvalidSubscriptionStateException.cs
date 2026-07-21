using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a lifecycle/plan-change action is requested from a subscription state that does not
/// legally allow it (e.g. resuming a subscription that is not paused). No provider call is made.
/// </summary>
public class InvalidSubscriptionStateException : Exception
{
    public BillingSubscriptionState CurrentState { get; }
    public string AttemptedAction { get; }

    public InvalidSubscriptionStateException(string attemptedAction, BillingSubscriptionState currentState)
        : base($"Cannot {attemptedAction} subscription while it is in state '{currentState}'.")
    {
        AttemptedAction = attemptedAction;
        CurrentState = currentState;
    }
}
