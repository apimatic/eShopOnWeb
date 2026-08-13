using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a caller tries to register a number the provider does not consider a usable
/// destination. Rejected at registration time rather than when a later message fails to go out.
/// </summary>
public class InvalidDestinationNumberException : Exception
{
    public InvalidDestinationNumberException(string message) : base(message) { }
}
