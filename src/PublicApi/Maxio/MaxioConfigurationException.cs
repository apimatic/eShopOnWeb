using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thrown when the Maxio integration is invoked without a valid configuration
/// (missing API key, subdomain or product family handle and no base URL override).
/// </summary>
public sealed class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message)
        : base(message)
    {
    }
}
