using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a caller tries to register a number the provider does not consider a usable SMS destination.
/// Surfaced to the API as a 400 Bad Request.
/// </summary>
public class InvalidContactNumberException : Exception
{
    public InvalidContactNumberException(string message) : base(message) { }
}
