using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The requested lifecycle transition is not legal from the subscription's current state.</summary>
public class InvalidSubscriptionTransitionException : Exception
{
    public InvalidSubscriptionTransitionException(string currentState, string requestedAction)
        : base($"Cannot {requestedAction} a subscription that is currently {currentState}")
    {
    }
}
