using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"Order {orderId} does not exist.")
    {
    }
}
