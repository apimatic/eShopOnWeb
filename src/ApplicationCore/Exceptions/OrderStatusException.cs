using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order is asked to make an illegal state transition (for example dispatching a
/// cancelled order). Mapped to an HTTP 409 (Conflict) by the API layer.
/// </summary>
public class OrderStatusException : Exception
{
    public OrderStatusException(string message) : base(message)
    {
    }
}
