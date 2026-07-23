using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a requested subscription operation is rejected before any provider call is made —
/// an illegal lifecycle transition, a no-op plan change, or invalid usage input.
/// </summary>
public class InvalidSubscriptionOperationException : Exception
{
    public InvalidSubscriptionOperationException(string message)
        : base(message)
    {
    }

    public InvalidSubscriptionOperationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
