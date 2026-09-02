using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId) : base($"Order {orderId} was not found.")
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}
