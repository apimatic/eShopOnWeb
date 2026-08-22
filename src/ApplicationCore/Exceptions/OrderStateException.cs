using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderStateException : Exception
{
    public OrderStateException(string message) : base(message)
    {
    }
}
