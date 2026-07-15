using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a subscription request is rejected before any billing-provider call is made
/// (e.g. an illegal lifecycle transition, a no-op plan change, or a stale proration preview).
/// </summary>
public class SubscriptionValidationException : Exception
{
    public SubscriptionValidationException(string message) : base(message)
    {
    }
}
