using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a requested subscription operation is rejected locally, before any provider call —
/// an illegal lifecycle transition, a no-op plan change, or invalid usage input.
/// </summary>
public class InvalidSubscriptionOperationException : Exception
{
    public InvalidSubscriptionOperationException(string message) : base(message)
    {
    }

    public InvalidSubscriptionOperationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
