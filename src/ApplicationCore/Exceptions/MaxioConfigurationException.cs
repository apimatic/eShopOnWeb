using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}
