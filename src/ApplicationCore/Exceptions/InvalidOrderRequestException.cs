using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order-placement request is not something we can turn into an order — no lines,
/// a non-positive quantity, or a catalog item id that does not exist.
/// </summary>
public class InvalidOrderRequestException : Exception
{
    public InvalidOrderRequestException(string message) : base(message)
    {
    }
}
