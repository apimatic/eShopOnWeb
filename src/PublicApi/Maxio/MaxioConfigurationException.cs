using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Raised when the Maxio configuration section is missing required values.
/// </summary>
public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}
