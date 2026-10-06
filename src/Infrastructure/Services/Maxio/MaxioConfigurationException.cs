using System;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// Thrown when the Maxio integration is not configured correctly.
/// </summary>
public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message)
        : base(message)
    {
    }
}
