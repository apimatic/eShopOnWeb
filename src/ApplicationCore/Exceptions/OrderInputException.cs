using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a request to place an order is not valid (no items, a non-positive quantity, or an
/// unknown catalog item id). Surfaced to the caller as a 400.
/// </summary>
public class OrderInputException : Exception
{
    public OrderInputException(string message) : base(message)
    {
    }
}
