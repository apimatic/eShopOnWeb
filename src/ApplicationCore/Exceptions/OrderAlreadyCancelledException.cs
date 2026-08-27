using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderAlreadyCancelledException : Exception
{
    public OrderAlreadyCancelledException(int orderId)
        : base($"Order {orderId} has already been cancelled.")
    {
    }
}
