using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Thrown when the site configured via Maxio:Subdomain / Maxio:ProductFamilyHandle does not
/// contain the resources the integration expects.
/// </summary>
public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}
