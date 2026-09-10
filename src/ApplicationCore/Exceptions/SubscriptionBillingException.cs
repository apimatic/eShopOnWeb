using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the upstream billing system rejects or fails a request. Surfaced to callers
/// as HTTP 502 (Bad Gateway) since the failure originates from an external dependency.
/// Infrastructure-level, provider-specific exceptions should derive from this type so the
/// API layer can map them without taking a dependency on the provider.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message) : base(message)
    {
    }

    public SubscriptionBillingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
