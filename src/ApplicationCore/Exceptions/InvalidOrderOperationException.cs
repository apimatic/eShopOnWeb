using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order lifecycle transition (dispatch/cancel) is not permitted from the order's
/// current <see cref="Entities.OrderAggregate.OrderStatus"/>.
/// </summary>
public class InvalidOrderOperationException : Exception
{
    public InvalidOrderOperationException(string message) : base(message)
    {
    }
}
