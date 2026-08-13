using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order request cannot be honoured — it has no lines, a non-positive quantity,
/// or references a catalog item that does not exist.
/// </summary>
public class InvalidOrderRequestException : Exception
{
    public InvalidOrderRequestException(string message) : base(message)
    {
    }
}
