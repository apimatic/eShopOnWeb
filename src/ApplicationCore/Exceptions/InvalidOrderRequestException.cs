using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a request to place an order cannot be honored — no items, a non-positive quantity,
/// or a reference to a catalog item that does not exist.
/// </summary>
public class InvalidOrderRequestException : Exception
{
    public InvalidOrderRequestException(string message) : base(message)
    {
    }
}
