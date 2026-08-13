using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Raised when an order cannot be created from the supplied lines (e.g. an unknown catalog item).</summary>
public class OrderCreationException : Exception
{
    public OrderCreationException(string message) : base(message)
    {
    }
}
