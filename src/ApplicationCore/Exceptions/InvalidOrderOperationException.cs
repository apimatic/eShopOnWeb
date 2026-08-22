using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidOrderOperationException : Exception
{
    public InvalidOrderOperationException(string message) : base(message)
    {
    }
}
