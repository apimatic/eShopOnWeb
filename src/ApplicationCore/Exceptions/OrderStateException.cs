using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderStateException : Exception
{
    public OrderStateException(int orderId, string message)
        : base($"Order {orderId}: {message}")
    {
    }
}
