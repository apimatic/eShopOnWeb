using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class ProviderOperationException : Exception
{
    public ProviderOperationException(string message) : base(message)
    {
    }
}
