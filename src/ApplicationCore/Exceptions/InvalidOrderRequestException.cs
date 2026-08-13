using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order cannot be placed from the request as given — for example it references a
/// catalog item that does not exist, or a non-positive quantity.
/// </summary>
public class InvalidOrderRequestException : Exception
{
    public InvalidOrderRequestException(string message) : base(message)
    {
    }
}
