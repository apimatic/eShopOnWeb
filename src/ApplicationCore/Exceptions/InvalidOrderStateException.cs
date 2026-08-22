using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidOrderStateException : Exception
{
    public InvalidOrderStateException(string message) : base(message)
    {
    }
}
