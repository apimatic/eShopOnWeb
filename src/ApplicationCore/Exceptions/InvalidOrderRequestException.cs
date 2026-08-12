using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order-placement request is malformed — no lines, a non-positive quantity, or a
/// catalog item that does not exist. Surfaced to the caller as a 400.
/// </summary>
public class InvalidOrderRequestException : Exception
{
    public InvalidOrderRequestException(string message) : base(message)
    {
    }
}
