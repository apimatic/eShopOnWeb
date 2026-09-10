using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the subscription billing integration is not configured (e.g. missing API
/// credentials). Surfaced to callers as HTTP 503 so the rest of the API stays available.
/// </summary>
public class SubscriptionConfigurationException : Exception
{
    public SubscriptionConfigurationException(string message) : base(message)
    {
    }
}
