using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a requested subscription action (plan change, pause/resume/cancel/reactivate,
/// usage recording) is not a legal transition from the subscription's current state. No provider
/// call is made when this is thrown.
/// </summary>
public class InvalidSubscriptionStateException : Exception
{
    public InvalidSubscriptionStateException(string message) : base(message)
    {
    }
}
