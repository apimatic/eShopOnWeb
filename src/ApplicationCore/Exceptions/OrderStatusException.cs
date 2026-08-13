using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order lifecycle transition is not allowed (e.g. dispatching a cancelled order).
/// </summary>
public class OrderStatusException : Exception
{
    public OrderStatusException(string message) : base(message)
    {
    }
}
