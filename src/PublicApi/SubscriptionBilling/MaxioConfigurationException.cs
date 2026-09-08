using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

/// <summary>
/// Thrown when the Maxio integration cannot be used because it is not configured correctly.
/// </summary>
public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message)
        : base(message)
    {
    }

    public MaxioConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
