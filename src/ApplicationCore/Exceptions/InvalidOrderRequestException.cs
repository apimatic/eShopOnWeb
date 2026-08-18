using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order request is not valid to place: no items, a non-positive quantity, or a
/// catalog item id that does not exist.
/// </summary>
public class InvalidOrderRequestException : Exception
{
    public InvalidOrderRequestException(string message) : base(message)
    {
    }
}
