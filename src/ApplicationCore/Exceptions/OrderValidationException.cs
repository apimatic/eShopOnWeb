using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderValidationException : Exception
{
    public OrderValidationException(string message) : base(message)
    {
    }
}
