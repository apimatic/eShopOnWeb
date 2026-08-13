using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order cannot be created from the supplied lines (empty order, unknown catalog item,
/// or a non-positive quantity).
/// </summary>
public class OrderCreationException : Exception
{
    public OrderCreationException(string message) : base(message)
    {
    }
}
