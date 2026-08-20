using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class ProviderUnavailableException : Exception
{
    public ProviderUnavailableException(string message) : base(message)
    {
    }

    public ProviderUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
