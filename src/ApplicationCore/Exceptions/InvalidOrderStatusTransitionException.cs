using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidOrderStatusTransitionException : Exception
{
    public InvalidOrderStatusTransitionException(OrderStatus current, OrderStatus requested)
        : base($"Cannot move an order from '{current}' to '{requested}'.")
    {
    }
}
