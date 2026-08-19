using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a caller's request is well-formed but semantically invalid
/// (e.g. references a catalog item that does not exist, or an empty order).
/// </summary>
public class InvalidRequestException : Exception
{
    public InvalidRequestException(string message) : base(message)
    {
    }
}
