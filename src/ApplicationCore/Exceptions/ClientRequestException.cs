using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class ClientRequestException : Exception
{
    public ClientRequestException(string message) : base(message)
    {
    }
}
