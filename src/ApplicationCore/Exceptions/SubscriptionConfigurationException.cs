using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the billing provider is not configured (e.g. missing credentials), so subscription
/// operations cannot proceed. The rest of the application still runs; only these endpoints fail.
/// </summary>
public class SubscriptionConfigurationException : Exception
{
    public SubscriptionConfigurationException(string message) : base(message)
    {
    }
}
