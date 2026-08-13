using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a shopper tries to register a number the provider does not consider a usable destination.
/// The offending number is deliberately not carried on the exception so it cannot leak into logs.
/// </summary>
public class InvalidPhoneNumberException : Exception
{
    public InvalidPhoneNumberException()
        : base("The phone number provided is not a valid, reachable destination.")
    {
    }

    public InvalidPhoneNumberException(string message) : base(message)
    {
    }
}
