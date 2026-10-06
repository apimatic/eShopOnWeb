using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}
