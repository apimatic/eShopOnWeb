using System;
using System.Text.Json;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Raised when the Maxio integration is not configured (missing API key or site) or when a
/// configured value cannot be used to reach the API.
/// </summary>
public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message)
        : base(message)
    {
    }
}
