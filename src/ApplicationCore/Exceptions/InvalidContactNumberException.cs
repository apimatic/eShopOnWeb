using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a shopper tries to register a number the provider does not consider a usable
/// destination. Mapped to an HTTP 400 (Bad Request) by the API layer. The rejected number is never
/// included in the message so it does not leak into logs.
/// </summary>
public class InvalidContactNumberException : Exception
{
    public InvalidContactNumberException(string message) : base(message)
    {
    }
}
