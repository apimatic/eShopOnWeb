using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class ProviderException : Exception
{
    public ProviderException(string message) : base(message)
    {
    }
}
