using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the Maxio integration is invoked but has not been configured (missing API key /
/// subdomain / product family handle). Signals a deployment/configuration problem rather than a
/// caller error.
/// </summary>
public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}
