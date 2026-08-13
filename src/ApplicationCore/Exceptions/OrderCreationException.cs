using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>A caller's order request could not be turned into an order (empty, or referencing unknown catalog items).</summary>
public class OrderCreationException : Exception
{
    public OrderCreationException(string message) : base(message)
    {
    }
}
