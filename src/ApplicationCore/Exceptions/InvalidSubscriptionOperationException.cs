using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a subscription request is rejected by eShopOnWeb's own rules before any provider
/// call is made — an illegal lifecycle transition, a no-op plan change, usage against a
/// subscription that is not active, or a stale plan-change preview.
/// </summary>
public class InvalidSubscriptionOperationException : Exception
{
    public InvalidSubscriptionOperationException(string message) : base(message)
    {
    }

    public InvalidSubscriptionOperationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
