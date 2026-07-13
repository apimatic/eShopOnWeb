using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Thrown when the requested lifecycle/plan-change transition is not legal from the subscription's current state.</summary>
public class InvalidSubscriptionStateException : Exception
{
    public InvalidSubscriptionStateException(string message) : base(message)
    {
    }
}
