using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidOrderTransitionException : Exception
{
    public InvalidOrderTransitionException(string message) : base(message)
    {
    }
}
