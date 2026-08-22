using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidOrderTransitionException : Exception
{
    public int OrderId { get; }

    public InvalidOrderTransitionException(int orderId, string message) : base(message)
    {
        OrderId = orderId;
    }
}
