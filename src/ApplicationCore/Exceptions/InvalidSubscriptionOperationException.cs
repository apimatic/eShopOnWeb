using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a subscription operation is rejected by the domain before any provider call is made —
/// an illegal lifecycle transition, a plan change to the plan already in effect, a non-positive usage
/// quantity, or usage reported for a user without an active subscription.
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
