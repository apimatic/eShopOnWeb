using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the upstream Maxio billing system fails or returns an unexpected response.
/// Surfaced to API callers as a 502 Bad Gateway.
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
