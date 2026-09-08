using System;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}
