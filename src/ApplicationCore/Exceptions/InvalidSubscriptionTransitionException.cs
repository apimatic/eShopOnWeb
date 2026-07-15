using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a lifecycle transition (pause/resume/cancel/reactivate) is illegal from the subscription's
/// current state (plan.md UC4). No provider call is made when this is thrown.
/// </summary>
public class InvalidSubscriptionTransitionException : Exception
{
    public InvalidSubscriptionTransitionException(string requestedTransition, string currentState)
        : base($"Cannot {requestedTransition} a subscription that is currently '{currentState}'")
    {
    }
}
