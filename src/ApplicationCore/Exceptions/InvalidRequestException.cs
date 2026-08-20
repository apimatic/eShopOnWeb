using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidRequestException : Exception
{
    public InvalidRequestException(string message) : base(message)
    {
    }
}
