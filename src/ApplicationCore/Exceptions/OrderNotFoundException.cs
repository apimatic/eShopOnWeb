using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId) : base($"No order found with id {orderId}")
    {
    }
}
