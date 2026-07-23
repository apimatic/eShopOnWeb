using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a requested subscription operation is rejected by eShopOnWeb's own rules before any
/// provider call is made — an illegal lifecycle transition, a plan change to the plan already in
/// use, a stale proration preview, or usage reported without an active subscription.
/// </summary>
public class InvalidSubscriptionOperationException : Exception
{
    public InvalidSubscriptionOperationException(string message) : base(message)
    {
    }
}
