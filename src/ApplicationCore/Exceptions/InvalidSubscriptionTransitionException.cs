using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The requested lifecycle/plan-change transition is not legal from the subscription's current state.</summary>
public class InvalidSubscriptionTransitionException : Exception
{
    public InvalidSubscriptionTransitionException(string message) : base(message)
    {
    }
}
