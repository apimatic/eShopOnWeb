using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order is asked to make a state transition that is not allowed
/// (for example, dispatching an order that has already been cancelled).
/// </summary>
public class OrderStatusException : Exception
{
    public OrderStatusException(string message) : base(message)
    {
    }
}
