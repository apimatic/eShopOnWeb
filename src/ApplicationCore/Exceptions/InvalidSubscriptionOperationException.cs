using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a subscription request is rejected by the domain before any provider call is made —
/// an illegal lifecycle transition, a no-op plan change, a non-positive usage quantity, or a preview
/// that no longer matches what the provider would charge.
/// </summary>
public class InvalidSubscriptionOperationException : Exception
{
    public InvalidSubscriptionOperationException(string message) : base(message)
    {
    }
}
