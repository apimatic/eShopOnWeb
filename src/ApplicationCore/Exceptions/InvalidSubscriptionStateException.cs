using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested lifecycle/plan-change action is not legal from the subscription's current state
/// (UC3/UC4). No provider call is made when this is thrown.
/// </summary>
public class InvalidSubscriptionStateException : Exception
{
    public string CurrentState { get; }
    public string RequestedAction { get; }

    public InvalidSubscriptionStateException(string currentState, string requestedAction)
        : base($"Cannot apply '{requestedAction}' to a subscription in state '{currentState}'")
    {
        CurrentState = currentState;
        RequestedAction = requestedAction;
    }
}
