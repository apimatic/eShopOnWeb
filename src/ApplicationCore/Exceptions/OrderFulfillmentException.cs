using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderFulfillmentException : Exception
{
    public OrderFulfillmentException(string message) : base(message)
    {
    }
}
