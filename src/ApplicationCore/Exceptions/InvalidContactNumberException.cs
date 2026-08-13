using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a shopper tries to register a number the provider does not consider a usable
/// destination. Deliberately carries no detail that could leak the number into a message or log.
/// </summary>
public class InvalidContactNumberException : Exception
{
    public InvalidContactNumberException()
        : base("The supplied number is not a valid, reachable destination.")
    {
    }
}
