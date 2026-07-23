using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a requested subscription operation is rejected locally, before any provider call is
/// made — an illegal lifecycle transition, a no-op plan change, or a non-positive usage quantity.
/// </summary>
public class InvalidSubscriptionOperationException : Exception
{
    public InvalidSubscriptionOperationException(string message) : base(message)
    {
    }
}
