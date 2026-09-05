using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class MaxioProviderException : Exception
{
    public MaxioProviderException(string message) : base(message)
    {
    }

    public MaxioProviderException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
