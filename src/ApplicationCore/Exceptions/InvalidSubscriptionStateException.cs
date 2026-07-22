using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested operation is not legal from the subscription's current state, so no provider
/// call is made. Carries the current state so the caller can tell the customer what to do next.
/// </summary>
public class InvalidSubscriptionStateException : Exception
{
    public InvalidSubscriptionStateException(string message, string currentState) : base(message)
    {
        CurrentState = currentState;
    }

    public string CurrentState { get; }
}
