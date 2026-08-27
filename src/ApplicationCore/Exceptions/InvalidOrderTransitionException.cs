using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidOrderTransitionException : Exception
{
    public InvalidOrderTransitionException(OrderStatus current, OrderStatus target)
        : base($"Cannot transition an order from '{current}' to '{target}'.")
    {
    }
}
