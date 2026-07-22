using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a subscription request is rejected by eShopOnWeb's own rules before the billing
/// provider is contacted — an illegal lifecycle transition, a no-op plan change, a stale
/// proration preview, or an invalid usage quantity.
/// </summary>
public class InvalidSubscriptionOperationException : Exception
{
    public InvalidSubscriptionOperationException(string message) : base(message)
    {
    }
}
