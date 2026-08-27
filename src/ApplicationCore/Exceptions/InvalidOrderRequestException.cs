using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidOrderRequestException : Exception
{
    public InvalidOrderRequestException(string message) : base(message)
    {
    }
}
