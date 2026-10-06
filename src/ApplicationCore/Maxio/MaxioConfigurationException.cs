using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}
