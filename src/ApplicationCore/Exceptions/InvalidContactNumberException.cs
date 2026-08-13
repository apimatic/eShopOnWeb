using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a shopper tries to register a number the provider does not consider a usable
/// destination. Carries a caller-safe reason and never echoes the number.
/// </summary>
public class InvalidContactNumberException : Exception
{
    public InvalidContactNumberException(string message) : base(message)
    {
    }
}
