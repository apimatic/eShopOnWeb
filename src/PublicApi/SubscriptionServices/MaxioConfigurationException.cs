using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionServices;

/// <summary>
/// Raised when the subscription capability is used while the Maxio configuration is incomplete.
/// The host still runs; only the Maxio-backed endpoints report this as 503 Service Unavailable.
/// </summary>
public sealed class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}
