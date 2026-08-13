using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Raised when an order is asked to make a transition its current <see cref="OrderStatus"/>
/// does not allow (for example dispatching an already-cancelled order).
/// </summary>
public class InvalidOrderStateException : Exception
{
    public InvalidOrderStateException(string message) : base(message)
    {
    }
}
