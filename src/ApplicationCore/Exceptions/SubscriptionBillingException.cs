using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the upstream billing system (Maxio Advanced Billing) returns an error or is
/// unreachable. Surfaced to API callers as a bad-gateway response.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message) : base(message)
    {
    }

    public SubscriptionBillingException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
