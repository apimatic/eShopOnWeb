using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a subscription operation is rejected locally — an illegal lifecycle transition, a no-op
/// plan change, or invalid usage input — before any provider call is made.
/// </summary>
public class InvalidSubscriptionOperationException : Exception
{
    public InvalidSubscriptionOperationException(string message) : base(message)
    {
    }
}
