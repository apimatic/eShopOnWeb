using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the provider does not consider a submitted number a usable SMS destination.
/// The raw number is never included in the message so it cannot leak into logs.
/// </summary>
public class InvalidPhoneNumberException : Exception
{
    public InvalidPhoneNumberException()
        : base("The supplied phone number is not a valid, reachable SMS destination.")
    {
    }

    public InvalidPhoneNumberException(string message) : base(message)
    {
    }
}
