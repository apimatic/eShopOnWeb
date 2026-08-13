using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Thrown when a request to place an order is not valid (no items, or an unknown catalog item).</summary>
public class OrderCreationException : Exception
{
    public OrderCreationException(string message) : base(message)
    {
    }
}
