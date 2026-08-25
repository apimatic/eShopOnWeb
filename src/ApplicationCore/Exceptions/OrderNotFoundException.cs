using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderNotFoundException : Exception
{
    public int OrderId { get; }

    public OrderNotFoundException(int orderId) : base($"Order {orderId} was not found.")
    {
        OrderId = orderId;
    }
}
